using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Emby.Plugin.MDBList.Sync;
using MediaBrowser.Model.Tasks;

namespace Emby.Plugin.MDBList.ScheduledTasks;

/// <summary>
/// Cheap, frequent poll: checks /sync/last_activities and only pulls when a
/// relevant bucket actually advanced -- port of
/// jellyfin-plugin-mdblist's MDBListActivityCheckTask.cs (itself
/// sync_orchestrator.py's check_activity()).
///
/// Note the signature: Emby's <c>IScheduledTask.Execute</c> takes
/// <c>(CancellationToken, IProgress&lt;double&gt;)</c> -- reversed parameter
/// order and a different method name than Jellyfin's
/// <c>ExecuteAsync(IProgress&lt;double&gt;, CancellationToken)</c>, confirmed
/// by reflection. Easy to get wrong copy-pasting from the Jellyfin sibling.
/// </summary>
public class MDBListActivityCheckTask : IScheduledTask
{
    private readonly SyncOrchestrator _orchestrator;

    /// <summary>
    /// Initializes a new instance of the <see cref="MDBListActivityCheckTask"/> class.
    /// </summary>
    /// <param name="orchestrator">Instance of the <see cref="SyncOrchestrator"/>.</param>
    public MDBListActivityCheckTask(SyncOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    /// <inheritdoc />
    public string Name => "MDBList Activity Check";

    /// <inheritdoc />
    public string Key => "MDBListActivityCheck";

    /// <inheritdoc />
    public string Description => "Checks MDBList for new activity and pulls watched-status/ratings changes if anything changed.";

    /// <inheritdoc />
    public string Category => "MDBList";

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return
        [
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfo.TriggerInterval,
                IntervalTicks = TimeSpan.FromMinutes(15).Ticks,
            },
        ];
    }

    /// <inheritdoc />
    public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
    {
        await _orchestrator.CheckActivityAsync(cancellationToken).ConfigureAwait(false);
        progress.Report(100);
    }
}
