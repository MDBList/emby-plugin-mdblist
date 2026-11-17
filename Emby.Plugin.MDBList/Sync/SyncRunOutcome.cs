namespace Emby.Plugin.MDBList.Sync;

/// <summary>
/// How a full sync run ended -- kept apart so a failure (e.g. MDBList's
/// daily quota) isn't reported like a run that had nothing to do. Ordered
/// from best to worst, so combining several runs keeps the highest.
/// </summary>
public enum SyncRunOutcome
{
    /// <summary>
    /// Nothing ran: another run held the lock, the user isn't linked or has
    /// no valid token, or the empty-library guard stopped it.
    /// </summary>
    Skipped,

    /// <summary>
    /// The run finished.
    /// </summary>
    Completed,

    /// <summary>
    /// The run started but an MDBList API call failed.
    /// </summary>
    Failed,
}
