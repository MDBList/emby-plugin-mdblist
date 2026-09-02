using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Logging;

namespace Emby.Plugin.MDBList;

/// <summary>
/// Server startup hook -- Emby's analogue of Jellyfin's <c>IHostedService</c>.
/// Auto-discovered by implementing <see cref="IServerEntryPoint"/>; no
/// explicit registration needed. Unlike Jellyfin, there is no
/// IPluginServiceRegistrator equivalent -- confirmed live (Phase 0) that
/// Emby's own container resolves plain, unregistered concrete classes via
/// their greediest public constructor anyway, so the whole sync engine
/// (SyncOrchestrator, WatchedSync, ...) can be constructor-injected here
/// exactly the way it is in the Jellyfin plugin, with no manual composition
/// root needed.
/// </summary>
public class EntryPoint : IServerEntryPoint
{
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="EntryPoint"/> class.
    /// </summary>
    /// <param name="logManager">Instance of the <see cref="ILogManager"/> interface.</param>
    public EntryPoint(ILogManager logManager)
    {
        _logger = logManager.GetLogger("MDBList");
    }

    /// <inheritdoc />
    public void Run()
    {
        _logger.Info("MDBList EntryPoint.Run() called.");
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }
}
