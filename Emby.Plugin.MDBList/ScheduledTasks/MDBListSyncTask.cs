using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Emby.Plugin.MDBList.Sync;
using MediaBrowser.Model.Tasks;

namespace Emby.Plugin.MDBList.ScheduledTasks;

/// <summary>
/// Full push-and-pull reconciliation for every enabled category -- the
/// periodic backstop that covers anything the live listener and the cheap
/// activity poll missed. Port of jellyfin-plugin-mdblist's
/// MDBListSyncTask.cs.
/// </summary>
public class MDBListSyncTask : IScheduledTask
{
    private readonly SyncOrchestrator _orchestrator;

    /// <summary>
    /// Initializes a new instance of the <see cref="MDBListSyncTask"/> class.
    /// </summary>
    /// <param name="orchestrator">Instance of the <see cref="SyncOrchestrator"/>.</param>
    public MDBListSyncTask(SyncOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    /// <inheritdoc />
    public string Name => Resources.Task_Sync_Name;

    /// <inheritdoc />
    public string Key => "MDBListSync";

    /// <inheritdoc />
    public string Description => Resources.Task_Sync_Desc;

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
                IntervalTicks = TimeSpan.FromHours(24).Ticks,
            },
        ];
    }

    /// <inheritdoc />
    public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
    {
        // allowRemovals: true -- the deliberate 24h reconciliation backstop,
        // trusted to actually remove things from MDBList (still subject to
        // SyncPayloadBuilder's magnitude circuit-breaker).
        await _orchestrator.RunAllLinkedUsersAsync(allowRemovals: true, cancellationToken).ConfigureAwait(false);
        progress.Report(100);
    }
}
