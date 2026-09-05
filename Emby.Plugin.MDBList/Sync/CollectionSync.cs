using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Emby.Plugin.MDBList.Library;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;

namespace Emby.Plugin.MDBList.Sync;

/// <summary>
/// Collection/library membership push -- port of jellyfin-plugin-mdblist's
/// CollectionSync.cs. Emby -> MDBList only: this reflects what's actually
/// in the local library so MDBList's collected status is accurate. There is
/// deliberately no pull direction -- Emby can't materialize a file just
/// because MDBList thinks it's collected, so a remote-only "collected" flag
/// has nothing local to apply.
///
/// Carries the same safety guard the Kodi addon didn't need: a disappeared
/// network mount can make items legitimately, successfully disappear from a
/// query, which a diff would otherwise read as "the user deleted
/// everything" and push a mass removal. <c>ITaskManager</c>/<c>TaskState</c>
/// live in <c>MediaBrowser.Model.Tasks</c> here (Jellyfin: same
/// namespace); the "RefreshLibrary" scan-task key is confirmed identical
/// on both platforms.
/// </summary>
public class CollectionSync
{
    private const SyncCategory Category = SyncCategory.Collection;
    private const string Endpoint = "/sync/collection";
    private const string RemoveEndpoint = "/sync/collection/remove";
    private const string FieldName = "collected_at";
    private const string LibraryScanTaskKey = "RefreshLibrary";

    /// <summary>
    /// A collection shrinking by more than this fraction versus the last
    /// known count refuses removals, same idea as the empty-snapshot guard
    /// but for a partial (not total) mount/permissions failure.
    /// </summary>
    private const double MaxShrinkFraction = 0.20;

    private readonly SyncPayloadBuilder _payloadBuilder;
    private readonly SyncStateStore _stateStore;
    private readonly ITaskManager _taskManager;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CollectionSync"/> class.
    /// </summary>
    /// <param name="payloadBuilder">Instance of the <see cref="SyncPayloadBuilder"/>.</param>
    /// <param name="stateStore">Instance of the <see cref="SyncStateStore"/>.</param>
    /// <param name="taskManager">Instance of the <see cref="ITaskManager"/> interface.</param>
    /// <param name="logManager">Instance of the <see cref="ILogManager"/> interface.</param>
    public CollectionSync(SyncPayloadBuilder payloadBuilder, SyncStateStore stateStore, ITaskManager taskManager, ILogManager logManager)
    {
        _payloadBuilder = payloadBuilder;
        _stateStore = stateStore;
        _taskManager = taskManager;
        _logger = logManager.GetLogger("MDBList.Collection");
    }

    /// <summary>
    /// Push + reconcile: anything newly present in the library is added,
    /// anything that dropped out (file removed/library item deleted) since
    /// the last run is removed from MDBList's collection -- unless the
    /// safety guard determines this run's snapshot looks more like a
    /// transient failure than a real removal, in which case only additions
    /// are pushed this time.
    /// </summary>
    /// <param name="userId">The Emby user.</param>
    /// <param name="accessToken">A valid MDBList access token.</param>
    /// <param name="snapshot">The current library snapshot.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>How many items were pushed as added/removed.</returns>
    public async Task<PushResult> PushAsync(Guid userId, string accessToken, LibrarySnapshot snapshot, CancellationToken cancellationToken)
    {
        var current = CurrentCollectedItems(snapshot);
        var known = await _stateStore.GetKnownItemsAsync(userId, Category, cancellationToken).ConfigureAwait(false);
        var allowRemovals = IsSafeToRemove(current.Count, known.Count);

        return await _payloadBuilder.DiffAndReconcileAsync(
            userId,
            Category,
            current,
            items => _payloadBuilder.PushItemsAsync(userId, Category, accessToken, Endpoint, FieldName, items, GetCollectedAtValue, cancellationToken),
            items => _payloadBuilder.PushItemsRemoveAsync(userId, Category, accessToken, RemoveEndpoint, items, cancellationToken),
            valueChanged: null,
            cancellationToken,
            allowRemovals).ConfigureAwait(false);
    }

    private bool IsSafeToRemove(int currentCount, int knownCount)
    {
        if (knownCount == 0)
        {
            // Nothing tracked yet -- there's no "previous collection" a
            // removal could be wrongly clobbering.
            return true;
        }

        if (currentCount == 0)
        {
            _logger.Warn(
                "MDBList Collection Sync: refusing to push removals -- snapshot is empty but {0} items were previously collected. "
                    + "This usually means a library query failed rather than the library actually being empty.",
                knownCount);
            return false;
        }

        var shrinkFraction = (double)(knownCount - currentCount) / knownCount;
        if (shrinkFraction > MaxShrinkFraction)
        {
            _logger.Warn(
                "MDBList Collection Sync: refusing to push removals -- collection shrank by {0:P0} ({1} -> {2}), "
                    + "more than the {3:P0} threshold. This usually means a mount or permissions issue rather than a real bulk delete.",
                shrinkFraction,
                knownCount,
                currentCount,
                MaxShrinkFraction);
            return false;
        }

        if (_taskManager.ScheduledTasks.Any(t => t.ScheduledTask.Key == LibraryScanTaskKey && t.State == TaskState.Running))
        {
            _logger.Info("MDBList Collection Sync: refusing to push removals -- a library scan is currently in progress");
            return false;
        }

        return true;
    }

    private static JsonNode? GetCollectedAtValue(KnownSyncItem item)
    {
        return item.CollectedAt is null ? null : JsonValue.Create(item.CollectedAt);
    }

    private static KnownSyncItem BuildKnownItem(SnapshotItem record)
    {
        return new KnownSyncItem
        {
            Type = record.Type,
            Ids = record.Ids,
            Season = record.Season,
            Episode = record.EpisodeNumber,
            CollectedAt = record.DateCreated.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
        };
    }

    private static Dictionary<string, KnownSyncItem> CurrentCollectedItems(LibrarySnapshot snapshot)
    {
        var items = new Dictionary<string, KnownSyncItem>(StringComparer.Ordinal);

        // Every item in the snapshot is already a real, non-virtual library
        // entry (LibrarySnapshot's own query filters IsVirtualItem=false),
        // so everything present is collected.
        foreach (var movie in snapshot.Movies)
        {
            var key = ItemKeys.CanonicalMovieKey(movie.Ids);
            if (key is not null)
            {
                items[key] = BuildKnownItem(movie);
            }
        }

        foreach (var episode in snapshot.Episodes)
        {
            var key = ItemKeys.CanonicalEpisodeKey(episode.Ids, episode.Season, episode.EpisodeNumber);
            if (key is not null)
            {
                items[key] = BuildKnownItem(episode);
            }
        }

        return items;
    }
}
