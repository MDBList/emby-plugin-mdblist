using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emby.Plugin.MDBList.Api;
using Emby.Plugin.MDBList.Sync;
using Emby.Web.GenericEdit.Common;
using Emby.Web.GenericEdit.Elements;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Plugins.UI.Views;
using MediaBrowser.Model.Querying;

namespace Emby.Plugin.MDBList.UI;

/// <summary>
/// The plugin's config page view -- device-code OAuth connect/disconnect/
/// test, per-user sync-category toggles, and manual "Sync now", all driven
/// through <see cref="MDBListOptions"/>.
///
/// <see cref="ContentData"/> is replaced with a freshly-deserialized
/// instance by the host before every <see cref="RunCommand"/> call
/// (confirmed by decompiling <c>PageControllerHostBase.RunCommand</c>) --
/// every handler here reads it fresh via the <see cref="Options"/> property
/// rather than capturing a local reference, since a background operation
/// (the device-code poll loop) spans multiple such replacements.
/// </summary>
public class MDBListPageView : PluginPageView
{
    private readonly IUserManager _userManager;
    private readonly OAuthService _oauthService;
    private readonly SyncOrchestrator _orchestrator;
    private readonly SyncStateStore _stateStore;
    private readonly ILogger _logger;
    private CancellationTokenSource? _connectCts;

    /// <summary>
    /// Initializes a new instance of the <see cref="MDBListPageView"/> class.
    /// </summary>
    /// <param name="pluginId">The owning plugin's id.</param>
    /// <param name="userManager">Instance of the <see cref="IUserManager"/> interface.</param>
    /// <param name="oauthService">Instance of the <see cref="OAuthService"/>.</param>
    /// <param name="orchestrator">Instance of the <see cref="SyncOrchestrator"/>.</param>
    /// <param name="stateStore">Instance of the <see cref="SyncStateStore"/>.</param>
    /// <param name="logManager">Instance of the <see cref="ILogManager"/> interface.</param>
    public MDBListPageView(
        string pluginId,
        IUserManager userManager,
        OAuthService oauthService,
        SyncOrchestrator orchestrator,
        SyncStateStore stateStore,
        ILogManager logManager)
        : base(pluginId)
    {
        _userManager = userManager;
        _oauthService = oauthService;
        _orchestrator = orchestrator;
        _stateStore = stateStore;
        _logger = logManager.GetLogger("MDBList.UI");

        var users = _userManager.GetUserList(new UserQuery());
        var availableUsers = users
            .Select(u => new EditorSelectOption(
                value: u.Id.ToString(),
                name: u.Name,
                isEnabled: true,
                color: null,
                displayHint: null,
                toolTip: null,
                filterValue: null))
            .ToList();

        ContentData = new MDBListOptions { AvailableUsers = availableUsers };

        var linkedUserId = Plugin.Instance?.Configuration.Users.FirstOrDefault()?.EmbyUserId;
        var initialUserId = linkedUserId ?? users.FirstOrDefault()?.Id ?? Guid.Empty;
        Options.SelectedUserId = initialUserId.ToString();
    }

    private MDBListOptions Options => (ContentData as MDBListOptions)!;

    private Guid CurrentUserId => Guid.TryParse(Options.SelectedUserId, out var id) ? id : Guid.Empty;

    /// <summary>
    /// Loads the initially-selected user's full state -- split out from the
    /// constructor since it awaits <see cref="SyncStateStore"/>.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task InitializeAsync() => LoadUserStateAsync(CurrentUserId);

    /// <inheritdoc />
    public override Task<IPluginUIView> RunCommand(string itemId, string commandId, string data)
    {
        switch (commandId)
        {
            case "UserChanged":
                // CurrentUserId reads Options.SelectedUserId off the freshly
                // deserialized ContentData (the full posted form state),
                // rather than itemId (the AutoPostBack param value) -- the
                // two should agree, but empirically itemId lagged one
                // interaction behind on a real client, causing the
                // selection to revert on the first change and only stick
                // on a second, identical one.
                return LoadUserStateAndReturnAsync(CurrentUserId);

            case "Connect":
                Task.Run(() => RunDeviceFlowAsync(CurrentUserId));
                return Task.FromResult<IPluginUIView>(this);

            case "Disconnect":
                Task.Run(() => HandleDisconnectAsync(CurrentUserId));
                return Task.FromResult<IPluginUIView>(this);

            case "Test":
                Task.Run(() => HandleTestAsync(CurrentUserId));
                return Task.FromResult<IPluginUIView>(this);

            case "SyncNow":
                Task.Run(() => HandleSyncNowAsync(CurrentUserId));
                return Task.FromResult<IPluginUIView>(this);
        }

        // Unrecognized here -- let the host handle its own built-ins
        // (PageSave -> OnSaveCommand, PageBack -> OnBackCommand). Swallowing
        // an unmatched command instead of falling through would silently
        // break the page's own Save button.
        return base.RunCommand(itemId, commandId, data);
    }

    /// <inheritdoc />
    public override Task<IPluginUIView> OnSaveCommand(string itemId, string commandId, string data)
    {
        var userId = CurrentUserId;
        var plugin = Plugin.Instance;
        var config = plugin?.Configuration.Users.FirstOrDefault(u => u.EmbyUserId == userId);
        if (config is not null)
        {
            config.WatchedEnabled = Options.WatchedEnabled;
            config.RatingsEnabled = Options.RatingsEnabled;
            config.CollectionEnabled = Options.CollectionEnabled;
            config.ScrobblingEnabled = Options.ScrobblingEnabled;
            config.IgnoreThumbRatings = Options.IgnoreThumbRatings;
            config.SyncAfterLibraryScan = Options.SyncAfterLibraryScan;
            plugin!.SaveConfiguration();
        }

        return base.OnSaveCommand(itemId, commandId, data);
    }

    private async Task<IPluginUIView> LoadUserStateAndReturnAsync(Guid userId)
    {
        await LoadUserStateAsync(userId).ConfigureAwait(false);
        return this;
    }

    private async Task LoadUserStateAsync(Guid userId)
    {
        var opts = Options;
        opts.SelectedUserId = userId.ToString();

        var config = Plugin.Instance?.Configuration.Users.FirstOrDefault(u => u.EmbyUserId == userId);
        var connected = config is not null && !string.IsNullOrEmpty(config.AccessToken);

        opts.ConnectionStatus.StatusText = connected ? "Connected to MDBList." : "Not connected.";
        opts.ConnectionStatus.Status = connected ? ItemStatus.Succeeded : ItemStatus.Unavailable;

        opts.DeviceCode.IsVisible = false;
        opts.VerifyLink.IsVisible = false;
        opts.ConnectButton.IsVisible = !connected;
        opts.ConnectButton.IsEnabled = true;
        opts.DisconnectButton.IsVisible = connected;
        opts.TestButton.IsVisible = connected;
        opts.SettingsCaption.IsVisible = connected;
        opts.StatusCaption.IsVisible = connected;
        opts.SyncNowButton.IsVisible = connected;

        opts.WatchedEnabled = config?.WatchedEnabled ?? true;
        opts.RatingsEnabled = config?.RatingsEnabled ?? true;
        opts.CollectionEnabled = config?.CollectionEnabled ?? true;
        opts.ScrobblingEnabled = config?.ScrobblingEnabled ?? true;
        opts.IgnoreThumbRatings = config?.IgnoreThumbRatings ?? true;
        opts.SyncAfterLibraryScan = config?.SyncAfterLibraryScan ?? true;

        if (connected)
        {
            var summary = await _stateStore.GetLastRunSummaryAsync(userId, CancellationToken.None).ConfigureAwait(false);
            opts.LastRunStatus.StatusText = summary ?? "No sync has run yet.";
            opts.LastRunStatus.Status = summary is null ? ItemStatus.Unavailable : ItemStatus.Succeeded;
        }
        else
        {
            opts.LastRunStatus.StatusText = "No sync has run yet.";
            opts.LastRunStatus.Status = ItemStatus.Unavailable;
        }
    }

    private async Task RunDeviceFlowAsync(Guid userId)
    {
        _connectCts?.Cancel();
        var cts = new CancellationTokenSource();
        _connectCts = cts;
        var ct = cts.Token;

        try
        {
            Options.ConnectButton.IsEnabled = false;
            SetConnectionStatus("Requesting device code...", ItemStatus.InProgress);

            var deviceCode = await _oauthService.StartDeviceAuthorizationAsync(ct).ConfigureAwait(false);

            var opts = Options;
            opts.DeviceCode.Text = deviceCode.UserCode;
            opts.DeviceCode.IsVisible = true;
            opts.VerifyLink.Text = deviceCode.VerificationUri;
            opts.VerifyLink.HyperLink = deviceCode.VerificationUriComplete;
            opts.VerifyLink.IsVisible = true;
            SetConnectionStatus(
                $"Enter code {deviceCode.UserCode} at {deviceCode.VerificationUri}, then wait for confirmation...",
                ItemStatus.InProgress);

            var deadline = DateTime.UtcNow.AddSeconds(deviceCode.ExpiresIn);
            var interval = TimeSpan.FromSeconds(Math.Max(deviceCode.Interval, 5));

            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(interval, ct).ConfigureAwait(false);

                var poll = await _oauthService.PollTokenAsync(userId, deviceCode.DeviceCode, ct).ConfigureAwait(false);
                switch (poll.Status)
                {
                    case "authorized":
                        await LoadUserStateAsync(userId).ConfigureAwait(false);
                        RaiseUIViewInfoChanged();
                        return;

                    case "expired":
                    case "denied":
                        Options.DeviceCode.IsVisible = false;
                        Options.VerifyLink.IsVisible = false;
                        Options.ConnectButton.IsEnabled = true;
                        SetConnectionStatus(poll.Message ?? "Authorization failed.", ItemStatus.Failed);
                        return;

                    case "slow_down":
                        interval += TimeSpan.FromSeconds(5);
                        break;
                }
            }

            Options.DeviceCode.IsVisible = false;
            Options.VerifyLink.IsVisible = false;
            Options.ConnectButton.IsEnabled = true;
            SetConnectionStatus("Authorization expired. Please try again.", ItemStatus.Failed);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer "Connect" click -- that flow owns the UI now.
        }
        catch (MDBListApiException ex)
        {
            Options.ConnectButton.IsEnabled = true;
            SetConnectionStatus($"Connection failed: {ex.Message}", ItemStatus.Failed);
        }
        catch (Exception ex)
        {
            _logger.ErrorException("MDBList UI: device authorization flow failed", ex);
            Options.ConnectButton.IsEnabled = true;
            SetConnectionStatus($"Connection failed: {ex.Message}", ItemStatus.Failed);
        }
        finally
        {
            if (ReferenceEquals(_connectCts, cts))
            {
                _connectCts = null;
            }
        }
    }

    private async Task HandleDisconnectAsync(Guid userId)
    {
        _connectCts?.Cancel();
        SetConnectionStatus("Disconnecting...", ItemStatus.InProgress);

        try
        {
            await _oauthService.DisconnectAsync(userId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.ErrorException("MDBList UI: disconnect failed", ex);
        }

        await LoadUserStateAsync(userId).ConfigureAwait(false);
        RaiseUIViewInfoChanged();
    }

    private async Task HandleTestAsync(Guid userId)
    {
        SetConnectionStatus("Testing connection...", ItemStatus.InProgress);

        try
        {
            var serverTime = await _oauthService.TestConnectionAsync(userId, CancellationToken.None).ConfigureAwait(false);
            SetConnectionStatus($"Connected. MDBList server time: {serverTime}", ItemStatus.Succeeded);
        }
        catch (MDBListApiException ex)
        {
            SetConnectionStatus($"Connection test failed: {ex.Message}", ItemStatus.Failed);
        }
    }

    private async Task HandleSyncNowAsync(Guid userId)
    {
        SetLastRunStatus("Syncing...", ItemStatus.InProgress);

        try
        {
            await _orchestrator.RunAsync(userId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.ErrorException("MDBList UI: manual sync failed", ex);
        }

        var summary = await _stateStore.GetLastRunSummaryAsync(userId, CancellationToken.None).ConfigureAwait(false);
        SetLastRunStatus(summary ?? "No sync has run yet.", summary is null ? ItemStatus.Unavailable : ItemStatus.Succeeded);
    }

    private void SetConnectionStatus(string text, ItemStatus status)
    {
        Options.ConnectionStatus.StatusText = text;
        Options.ConnectionStatus.Status = status;
        RaiseUIViewInfoChanged();
    }

    private void SetLastRunStatus(string text, ItemStatus status)
    {
        Options.LastRunStatus.StatusText = text;
        Options.LastRunStatus.Status = status;
        RaiseUIViewInfoChanged();
    }
}
