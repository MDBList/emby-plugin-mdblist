using Emby.Plugin.MDBList.Events;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Logging;

namespace Emby.Plugin.MDBList;

/// <summary>
/// Server startup hook -- Emby's analogue of Jellyfin's
/// <c>MDBListEventHostedService</c> (itself an <c>IHostedService</c>).
/// Auto-discovered by implementing <see cref="IServerEntryPoint"/>; no
/// explicit registration needed. Unlike Jellyfin, there is no
/// IPluginServiceRegistrator equivalent -- confirmed live (Phase 0) that
/// Emby's own container resolves plain, unregistered concrete classes via
/// their greediest public constructor anyway, so the whole sync engine
/// (SyncOrchestrator, WatchedSync, ...) is constructor-injected here
/// exactly the way it is in the Jellyfin plugin, with no manual composition
/// root needed.
/// </summary>
public class EntryPoint : IServerEntryPoint
{
    private readonly IUserDataManager _userDataManager;
    private readonly ILibraryManager _libraryManager;
    private readonly ISessionManager _sessionManager;
    private readonly LiveSyncService _liveSyncService;
    private readonly LibraryChangeDebouncer _libraryChangeDebouncer;
    private readonly PlaybackScrobbleService _playbackScrobbleService;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="EntryPoint"/> class.
    /// </summary>
    /// <param name="userDataManager">Instance of the <see cref="IUserDataManager"/> interface.</param>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="sessionManager">Instance of the <see cref="ISessionManager"/> interface.</param>
    /// <param name="liveSyncService">Instance of the <see cref="LiveSyncService"/>.</param>
    /// <param name="libraryChangeDebouncer">Instance of the <see cref="LibraryChangeDebouncer"/>.</param>
    /// <param name="playbackScrobbleService">Instance of the <see cref="PlaybackScrobbleService"/>.</param>
    /// <param name="logManager">Instance of the <see cref="ILogManager"/> interface.</param>
    public EntryPoint(
        IUserDataManager userDataManager,
        ILibraryManager libraryManager,
        ISessionManager sessionManager,
        LiveSyncService liveSyncService,
        LibraryChangeDebouncer libraryChangeDebouncer,
        PlaybackScrobbleService playbackScrobbleService,
        ILogManager logManager)
    {
        _userDataManager = userDataManager;
        _libraryManager = libraryManager;
        _sessionManager = sessionManager;
        _liveSyncService = liveSyncService;
        _libraryChangeDebouncer = libraryChangeDebouncer;
        _playbackScrobbleService = playbackScrobbleService;
        _logger = logManager.GetLogger("MDBList");
    }

    /// <inheritdoc />
    public void Run()
    {
        _userDataManager.UserDataSaved += OnUserDataSaved;
        _libraryManager.ItemAdded += OnLibraryItemChanged;
        _libraryManager.ItemUpdated += OnLibraryItemChanged;
        _libraryManager.ItemRemoved += OnLibraryItemChanged;
        _sessionManager.PlaybackStart += OnPlaybackStart;
        _sessionManager.PlaybackProgress += OnPlaybackProgress;
        _sessionManager.PlaybackStopped += OnPlaybackStopped;
        _logger.Info("MDBList: event subscriptions active.");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _userDataManager.UserDataSaved -= OnUserDataSaved;
        _libraryManager.ItemAdded -= OnLibraryItemChanged;
        _libraryManager.ItemUpdated -= OnLibraryItemChanged;
        _libraryManager.ItemRemoved -= OnLibraryItemChanged;
        _sessionManager.PlaybackStart -= OnPlaybackStart;
        _sessionManager.PlaybackProgress -= OnPlaybackProgress;
        _sessionManager.PlaybackStopped -= OnPlaybackStopped;
    }

    private void OnUserDataSaved(object? sender, UserDataSaveEventArgs e)
    {
        _liveSyncService.HandleUserDataSaved(e);
    }

    private void OnLibraryItemChanged(object? sender, ItemChangeEventArgs e)
    {
        _libraryChangeDebouncer.NotifyChange(e.Item);
    }

    private void OnPlaybackStart(object? sender, PlaybackProgressEventArgs e)
    {
        _playbackScrobbleService.HandlePlaybackStart(e);
    }

    private void OnPlaybackProgress(object? sender, PlaybackProgressEventArgs e)
    {
        _playbackScrobbleService.HandlePlaybackProgress(e);
    }

    private void OnPlaybackStopped(object? sender, PlaybackStopEventArgs e)
    {
        _playbackScrobbleService.HandlePlaybackStopped(e);
    }
}
