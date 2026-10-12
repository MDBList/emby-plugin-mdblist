namespace Emby.Plugin.MDBList.Sync;

/// <summary>
/// Structured record of the most recent sync run. Persisted as data (not as a
/// pre-rendered sentence) so it can be re-rendered in the requesting client's
/// language at display time -- the plugin has no way to know the Dashboard's
/// display language while a background run is executing.
/// </summary>
public class RunSummary
{
    /// <summary>
    /// Gets or sets a value indicating whether the run was the cheap
    /// activity-triggered pull (pull-only) rather than a full push-and-pull
    /// reconciliation.
    /// </summary>
    public bool IsActivityCheck { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the run was aborted because the
    /// local library snapshot came back empty while remote state was not --
    /// treated as an unreliable read rather than a real mass removal.
    /// </summary>
    public bool SkippedEmptyLibrary { get; set; }

    /// <summary>
    /// Gets or sets the error message when the run failed, or null on success.
    /// </summary>
    public string? FailedMessage { get; set; }

    /// <summary>
    /// Gets or sets the watched-status category result, or null if the
    /// category wasn't part of this run (activity check with no watched change).
    /// </summary>
    public CategoryRunSummary? Watched { get; set; }

    /// <summary>
    /// Gets or sets the ratings category result, or null if the category
    /// wasn't part of this run.
    /// </summary>
    public CategoryRunSummary? Ratings { get; set; }

    /// <summary>
    /// Gets or sets the collection category result, or null if the category
    /// wasn't part of this run.
    /// </summary>
    public CategoryRunSummary? Collection { get; set; }
}

/// <summary>
/// One category's contribution to a <see cref="RunSummary"/>. Push fields are
/// null for a pull-only activity check; pull fields are null for the collection
/// category (which only ever pushes) and for a skipped category.
/// </summary>
public class CategoryRunSummary
{
    /// <summary>
    /// Gets or sets a value indicating whether the category was disabled in the
    /// user's settings, so nothing was synced for it.
    /// </summary>
    public bool Skipped { get; set; }

    /// <summary>
    /// Gets or sets how many items were pushed as newly active, or null if no
    /// push phase ran.
    /// </summary>
    public int? PushedAdd { get; set; }

    /// <summary>
    /// Gets or sets how many items were pushed as removed, or null if no push
    /// phase ran.
    /// </summary>
    public int? PushedRemove { get; set; }

    /// <summary>
    /// Gets or sets how many pushed removals were held back, or null if no push
    /// phase ran.
    /// </summary>
    public int? PushSkippedRemove { get; set; }

    /// <summary>
    /// Gets or sets how many items were actually changed locally by the pull,
    /// or null if no pull phase ran.
    /// </summary>
    public int? PulledApplied { get; set; }

    /// <summary>
    /// Gets or sets how many local removals the pull held back, or null if no
    /// pull phase ran.
    /// </summary>
    public int? PullSkippedRemove { get; set; }

    /// <summary>
    /// Gets or sets the raw pull mode ("full"/"incremental"), localized only at
    /// render time, or null if no pull phase ran.
    /// </summary>
    public string? Mode { get; set; }
}