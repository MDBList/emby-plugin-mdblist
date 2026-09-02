using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Emby.Plugin.MDBList.Api.Models;
using Emby.Plugin.MDBList.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Model.Logging;
using HttpRequestOptions = MediaBrowser.Common.Net.HttpRequestOptions;

namespace Emby.Plugin.MDBList.Api;

/// <summary>
/// MDBList OAuth device-code flow -- port of the Kodi addon's oauth.py /
/// jellyfin-plugin-mdblist's OAuthService.cs, same endpoints and grant
/// types, against an Emby-specific client id.
/// </summary>
public class OAuthService : IDisposable
{
    private const string ClientId = "CjE9RaNzEPdJUe5pHScEcFnzfUi5yoJstAOSb08S";
    private const string DeviceAuthUrl = "https://api.mdblist.com/oauth/device-authorization/";
    private const string TokenUrl = "https://api.mdblist.com/oauth/token/";
    private const string RevokeUrl = "https://api.mdblist.com/oauth/revoke_token/";
    private const string DeviceGrantType = "urn:ietf:params:oauth:grant-type:device_code";

    // Serializes every read-modify-write of PluginConfiguration.Users -- the
    // dashboard's own "Save" button does a full config replace, so token
    // writes from the auth flow and refreshes must not race it or each other.
    private readonly SemaphoreSlim _configLock = new(1, 1);

    private readonly IHttpClient _httpClient;
    private readonly MDBListApiClient _apiClient;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OAuthService"/> class.
    /// </summary>
    /// <param name="httpClient">Instance of the <see cref="IHttpClient"/> interface.</param>
    /// <param name="apiClient">Instance of the <see cref="MDBListApiClient"/>.</param>
    /// <param name="logManager">Instance of the <see cref="ILogManager"/> interface.</param>
    public OAuthService(IHttpClient httpClient, MDBListApiClient apiClient, ILogManager logManager)
    {
        _httpClient = httpClient;
        _apiClient = apiClient;
        _logger = logManager.GetLogger("MDBList.OAuth");
    }

    /// <summary>
    /// Starts a device-authorization flow.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The device/user codes and verification URI.</returns>
    public async Task<DeviceCodeResult> StartDeviceAuthorizationAsync(CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["scope"] = "write",
        });

        var body = await PostAsync(DeviceAuthUrl, content, cancellationToken).ConfigureAwait(false);

        DeviceAuthorizationResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<DeviceAuthorizationResponse>(body);
        }
        catch (JsonException ex)
        {
            throw new MDBListApiException("Malformed device authorization response", ex);
        }

        if (parsed is null || string.IsNullOrEmpty(parsed.DeviceCode))
        {
            var message = parsed?.ErrorDescription ?? parsed?.Error ?? "Unknown error starting device authorization";
            throw new MDBListApiException(message);
        }

        var verificationUri = parsed.VerificationUri ?? "https://mdblist.com/oauth/device/";
        var verificationUriComplete = parsed.VerificationUriComplete
            ?? $"{verificationUri}?user_code={parsed.UserCode}";

        return new DeviceCodeResult
        {
            DeviceCode = parsed.DeviceCode,
            UserCode = parsed.UserCode ?? string.Empty,
            VerificationUri = verificationUri,
            VerificationUriComplete = verificationUriComplete,
            Interval = parsed.Interval,
            ExpiresIn = parsed.ExpiresIn,
        };
    }

    /// <summary>
    /// Makes one poll attempt against the token endpoint. On success, saves
    /// the tokens for <paramref name="embyUserId"/>.
    /// </summary>
    /// <param name="embyUserId">The Emby user to link the tokens to.</param>
    /// <param name="deviceCode">The device code from <see cref="StartDeviceAuthorizationAsync"/>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The poll status.</returns>
    public async Task<PollResult> PollTokenAsync(Guid embyUserId, string deviceCode, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = DeviceGrantType,
            ["device_code"] = deviceCode,
            ["client_id"] = ClientId,
        });

        TokenResponse? token;
        try
        {
            var body = await PostAsync(TokenUrl, content, cancellationToken).ConfigureAwait(false);
            token = JsonSerializer.Deserialize<TokenResponse>(body);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException)
        {
            // A single failed poll attempt isn't fatal -- the browser retries on
            // its own interval, same as oauth.py's poll loop swallowing a
            // request exception and continuing rather than aborting the flow.
            _logger.Debug("MDBList token poll attempt failed, will retry: {0}", ex.Message);
            return new PollResult { Status = "pending" };
        }

        if (token is null)
        {
            return new PollResult { Status = "pending" };
        }

        if (!string.IsNullOrEmpty(token.AccessToken))
        {
            await SaveTokensAsync(embyUserId, token, cancellationToken).ConfigureAwait(false);
            return new PollResult { Status = "authorized" };
        }

        return token.Error switch
        {
            "slow_down" => new PollResult { Status = "slow_down" },
            "expired_token" => new PollResult { Status = "expired", Message = "Authorization expired. Please try again." },
            "access_denied" => new PollResult { Status = "denied", Message = "Authorization was denied." },
            _ => new PollResult { Status = "pending" },
        };
    }

    /// <summary>
    /// Returns a valid access token for the given user, refreshing it first
    /// if it's within 5 minutes of expiry. Returns an empty string if the
    /// user isn't connected.
    /// </summary>
    /// <param name="embyUserId">The linked Emby user.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A valid access token, or an empty string.</returns>
    public async Task<string> EnsureValidTokenAsync(Guid embyUserId, CancellationToken cancellationToken)
    {
        var config = FindUserConfig(embyUserId);
        if (config is null || string.IsNullOrEmpty(config.AccessToken))
        {
            return string.Empty;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (config.ExpiresAt != 0 && now > config.ExpiresAt - 300)
        {
            var refreshed = await TryRefreshAsync(embyUserId, config.RefreshToken, cancellationToken).ConfigureAwait(false);
            if (refreshed)
            {
                config = FindUserConfig(embyUserId);
            }
        }

        return config?.AccessToken ?? string.Empty;
    }

    /// <summary>
    /// Revokes the stored token (best-effort) and clears it for the given user.
    /// </summary>
    /// <param name="embyUserId">The linked Emby user.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task DisconnectAsync(Guid embyUserId, CancellationToken cancellationToken)
    {
        var config = FindUserConfig(embyUserId);
        if (config is not null && !string.IsNullOrEmpty(config.AccessToken))
        {
            try
            {
                using var content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["token"] = config.AccessToken,
                    ["client_id"] = ClientId,
                });
                await PostAsync(RevokeUrl, content, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException)
            {
                _logger.Warn("MDBList token revoke failed, clearing local tokens anyway: {0}", ex.Message);
            }
        }

        await _configLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var plugin = Plugin.Instance ?? throw new InvalidOperationException("Plugin not initialized");
            var existing = plugin.Configuration.Users.FirstOrDefault(u => u.EmbyUserId == embyUserId);
            if (existing is not null)
            {
                plugin.Configuration.Users.Remove(existing);
            }

            plugin.SaveConfiguration();
        }
        finally
        {
            _configLock.Release();
        }
    }

    /// <summary>
    /// Calls /sync/last_activities to confirm the stored token actually works.
    /// </summary>
    /// <param name="embyUserId">The linked Emby user.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The server's own watermark timestamp.</returns>
    public async Task<string> TestConnectionAsync(Guid embyUserId, CancellationToken cancellationToken)
    {
        var accessToken = await EnsureValidTokenAsync(embyUserId, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(accessToken))
        {
            throw new MDBListApiException("Not connected to MDBList");
        }

        var activities = await _apiClient.FetchLastActivitiesAsync(accessToken, cancellationToken).ConfigureAwait(false);
        return activities.ServerTime ?? string.Empty;
    }

    private async Task<bool> TryRefreshAsync(Guid embyUserId, string refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(refreshToken))
        {
            return false;
        }

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = ClientId,
        });

        try
        {
            var body = await PostAsync(TokenUrl, content, cancellationToken).ConfigureAwait(false);
            var token = JsonSerializer.Deserialize<TokenResponse>(body);

            if (token is not null && !string.IsNullOrEmpty(token.AccessToken))
            {
                token.RefreshToken ??= refreshToken;
                await SaveTokensAsync(embyUserId, token, cancellationToken).ConfigureAwait(false);
                return true;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException)
        {
            _logger.ErrorException("MDBList token refresh failed", ex);
        }

        return false;
    }

    private async Task SaveTokensAsync(Guid embyUserId, TokenResponse token, CancellationToken cancellationToken)
    {
        await _configLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var plugin = Plugin.Instance ?? throw new InvalidOperationException("Plugin not initialized");
            var config = plugin.Configuration.Users.FirstOrDefault(u => u.EmbyUserId == embyUserId);
            if (config is null)
            {
                config = new UserSyncConfig { EmbyUserId = embyUserId };
                plugin.Configuration.Users.Add(config);
            }

            config.AccessToken = token.AccessToken ?? string.Empty;
            config.RefreshToken = token.RefreshToken ?? config.RefreshToken;
            config.ExpiresAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + token.ExpiresIn;

            plugin.SaveConfiguration();
        }
        finally
        {
            _configLock.Release();
        }
    }

    private static UserSyncConfig? FindUserConfig(Guid embyUserId)
    {
        return Plugin.Instance?.Configuration.Users.FirstOrDefault(u => u.EmbyUserId == embyUserId);
    }

    /// <summary>
    /// Posts a form body via Emby's <see cref="IHttpClient"/> abstraction
    /// and returns the response body as a string. Emby has no
    /// HttpClient/IHttpClientFactory of its own -- confirmed by reflecting
    /// over the real assemblies (see the plan) -- this is its equivalent.
    /// </summary>
    private async Task<string> PostAsync(string url, HttpContent content, CancellationToken cancellationToken)
    {
        var options = new HttpRequestOptions
        {
            Url = url,
            RequestHttpContent = content,
            CancellationToken = cancellationToken,
            ThrowOnErrorResponse = false,
        };

        using var response = await _httpClient.Post(options).ConfigureAwait(false);
        using var reader = new StreamReader(response.Content);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases the resources used by this instance.
    /// </summary>
    /// <param name="disposing">Whether managed resources should be released.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _configLock.Dispose();
        }
    }
}
