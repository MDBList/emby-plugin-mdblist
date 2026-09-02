using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emby.Plugin.MDBList.Api;
using Emby.Plugin.MDBList.Api.Models;
using Emby.Plugin.MDBList.Library;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Logging;

namespace Emby.Plugin.MDBList.Events;

/// <summary>
/// Live playback progress pushed to MDBList's /scrobble/* endpoints -- port
/// of jellyfin-plugin-mdblist's PlaybackScrobbleService.cs (itself
/// player_monitor.py). One-way and independent of watched-status sync: the
/// server uses this feed for its own watched-marking (at >=80% progress)
/// and "continue watching" position, so this never writes back to Emby and
/// needs none of WatchedSync's echo-loop guards.
///
/// <see cref="ISessionManager.PlaybackStart"/>/<c>PlaybackProgress</c>/
/// <c>PlaybackStopped</c> and their event arg shapes (<c>Item</c>,
/// <c>Users</c>, <c>PlaybackPositionTicks</c>, <c>IsPaused</c>,
/// <c>PlaySessionId</c>, <c>Session</c>, <c>PlayedToCompletion</c>) are
/// confirmed identical to Jellyfin's by reflection, so this logic ports
/// close to verbatim -- only the DI/logging plumbing changes.
/// </summary>
public sealed class PlaybackScrobbleService
{
    private const string StartEndpoint = "/scrobble/start";
    private const string PauseEndpoint = "/scrobble/pause";
    private const string StopEndpoint = "/scrobble/stop";

    // Emby's own automated progress reporting ticks frequently while a
    // session is playing -- without a floor here, every active playback
    // session would hammer /scrobble/start far more than needed.
    private static readonly TimeSpan MinProgressInterval = TimeSpan.FromSeconds(15);

    private readonly OAuthService _oauthService;
    private readonly MDBListApiClient _apiClient;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, SessionState> _sessions = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaybackScrobbleService"/> class.
    /// </summary>
    /// <param name="oauthService">Instance of the <see cref="OAuthService"/>.</param>
    /// <param name="apiClient">Instance of the <see cref="MDBListApiClient"/>.</param>
    /// <param name="logManager">Instance of the <see cref="ILogManager"/> interface.</param>
    public PlaybackScrobbleService(OAuthService oauthService, MDBListApiClient apiClient, ILogManager logManager)
    {
        _oauthService = oauthService;
        _apiClient = apiClient;
        _logger = logManager.GetLogger("MDBList.Scrobble");
    }

    /// <summary>
    /// Handles <c>ISessionManager.PlaybackStart</c>.
    /// </summary>
    /// <param name="e">The event args.</param>
    public void HandlePlaybackStart(PlaybackProgressEventArgs e)
    {
        var key = SessionKey(e);
        if (key is null)
        {
            return;
        }

        // Recorded synchronously, before the send below even runs: an
        // automated progress tick can arrive very soon after, and it must
        // see "just sent" immediately, not race the async send.
        _sessions[key] = new SessionState { LastSentAt = DateTime.UtcNow };
        Dispatch(e, StartEndpoint, playedToCompletion: false);
    }

    /// <summary>
    /// Handles <c>ISessionManager.PlaybackProgress</c> -- covers pause,
    /// resume, seek, and periodic progress ticks alike.
    /// </summary>
    /// <param name="e">The event args.</param>
    public void HandlePlaybackProgress(PlaybackProgressEventArgs e)
    {
        var key = SessionKey(e);
        if (key is null)
        {
            return;
        }

        var state = _sessions.GetOrAdd(key, static _ => new SessionState());

        if (e.IsPaused)
        {
            if (state.WasPaused)
            {
                return;
            }

            state.WasPaused = true;
            Dispatch(e, PauseEndpoint, playedToCompletion: false);
            return;
        }

        var resuming = state.WasPaused;
        state.WasPaused = false;

        var now = DateTime.UtcNow;
        if (!resuming && now - state.LastSentAt < MinProgressInterval)
        {
            return;
        }

        state.LastSentAt = now;
        Dispatch(e, StartEndpoint, playedToCompletion: false);
    }

    /// <summary>
    /// Handles <c>ISessionManager.PlaybackStopped</c> -- covers both a
    /// genuine stop and playing through to the end.
    /// </summary>
    /// <param name="e">The event args.</param>
    public void HandlePlaybackStopped(PlaybackStopEventArgs e)
    {
        var key = SessionKey(e);
        if (key is not null)
        {
            _sessions.TryRemove(key, out _);
        }

        Dispatch(e, StopEndpoint, e.PlayedToCompletion);
    }

    private static string? SessionKey(PlaybackProgressEventArgs e)
    {
        return e.PlaySessionId ?? e.Session?.Id;
    }

    private void Dispatch(PlaybackProgressEventArgs e, string endpoint, bool playedToCompletion)
    {
        _ = Task.Run(() => SendAsync(e, endpoint, playedToCompletion));
    }

    private async Task SendAsync(PlaybackProgressEventArgs e, string endpoint, bool playedToCompletion)
    {
        try
        {
            if (!TryResolveLinkedUserId(e.Users, out var userId))
            {
                return;
            }

            var linkedUserConfig = Plugin.Instance?.Configuration.Users.FirstOrDefault(u => u.EmbyUserId == userId);
            if (linkedUserConfig is null || !linkedUserConfig.ScrobblingEnabled)
            {
                return;
            }

            var request = BuildScrobbleRequest(e.Item, e.PlaybackPositionTicks, playedToCompletion);
            if (request is null)
            {
                return;
            }

            var accessToken = await _oauthService.EnsureValidTokenAsync(userId, CancellationToken.None).ConfigureAwait(false);
            if (string.IsNullOrEmpty(accessToken))
            {
                return;
            }

            await _apiClient.PushSyncItemsAsync(accessToken, endpoint, request, CancellationToken.None).ConfigureAwait(false);
        }
        catch (MDBListApiException ex)
        {
            _logger.Debug("MDBList scrobble push to {0} failed: {1}", endpoint, ex.Message);
        }
    }

    private static bool TryResolveLinkedUserId(IReadOnlyList<User> sessionUsers, out Guid userId)
    {
        var linkedUserConfig = Plugin.Instance?.Configuration.Users.FirstOrDefault();
        if (linkedUserConfig is null || !sessionUsers.Any(u => u.Id == linkedUserConfig.EmbyUserId))
        {
            userId = Guid.Empty;
            return false;
        }

        userId = linkedUserConfig.EmbyUserId;
        return true;
    }

    private static ScrobbleRequest? BuildScrobbleRequest(BaseItem? item, long? positionTicks, bool playedToCompletion)
    {
        if (item is null)
        {
            return null;
        }

        var progress = playedToCompletion ? 100.0 : ComputeProgress(item.RunTimeTicks, positionTicks);
        if (progress is null)
        {
            return null;
        }

        var appVersion = Plugin.Instance?.Version.ToString();

        if (item is Episode episode)
        {
            if (episode.Series is null || episode.ParentIndexNumber is null || episode.IndexNumber is null)
            {
                return null;
            }

            var showIds = MediaIdMapper.MapShowIds(episode.Series.ProviderIds);
            if (showIds.IsEmpty)
            {
                return null;
            }

            return new ScrobbleRequest
            {
                Show = new ScrobbleShowRef
                {
                    Ids = showIds,
                    Season = new ScrobbleSeasonRef
                    {
                        Number = episode.ParentIndexNumber,
                        Episode = new ScrobbleEpisodeRef { Number = episode.IndexNumber },
                    },
                },
                Progress = progress.Value,
                AppVersion = appVersion,
            };
        }

        if (item is Movie)
        {
            var ids = MediaIdMapper.MapMovieIds(item.ProviderIds);
            if (ids.IsEmpty)
            {
                return null;
            }

            return new ScrobbleRequest
            {
                Movie = new ScrobbleMovieRef { Ids = ids },
                Progress = progress.Value,
                AppVersion = appVersion,
            };
        }

        return null;
    }

    internal static double? ComputeProgress(long? runTimeTicks, long? positionTicks)
    {
        if (runTimeTicks is null or <= 0 || positionTicks is null)
        {
            return null;
        }

        var clamped = Math.Clamp(positionTicks.Value, 0, runTimeTicks.Value);
        return Math.Round((double)clamped / runTimeTicks.Value * 100, 2);
    }

    private sealed class SessionState
    {
        public bool WasPaused { get; set; }

        public DateTime LastSentAt { get; set; } = DateTime.MinValue;
    }
}
