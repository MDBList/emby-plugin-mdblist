using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Emby.Plugin.MDBList.Api;
using Emby.Plugin.MDBList.Api.Models;
using Emby.Plugin.MDBList.Library;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;

namespace Emby.Plugin.MDBList.Sync;

/// <summary>
/// Watched-status two-way sync -- port of jellyfin-plugin-mdblist's
/// WatchedSync.cs (itself watched_sync.py).
///
/// Push: membership diff, plus a value-changed check on WatchedAt so a
/// rewatch that only updates LastPlayedDate (Played unchanged) is still
/// re-pushed by the full diff, not just by the live single-item push from
/// <see cref="PushSingleAsync"/>.
///
/// Pull: real last-write-wins conflict resolution using UTC timestamps on
/// both sides. Unlike the Jellyfin sibling, Emby's <c>LastPlayedDate</c> is
/// <see cref="DateTimeOffset"/> not <see cref="DateTime"/> -- confirmed by
/// reflection -- so the remote timestamp is parsed to
/// <see cref="DateTimeOffset"/> here too rather than converted.
/// </summary>
public class WatchedSync
{
    private const SyncCategory Category = SyncCategory.Watched;
    private const string Endpoint = "/sync/watched";
    private const string RemoveEndpoint = "/sync/watched/remove";
    private const string FieldName = "watched_at";
    private const string JournalCategory = "watched";
    private const int JournalPageSize = 1000;

    private readonly SyncPayloadBuilder _payloadBuilder;
    private readonly SyncStateStore _stateStore;
    private readonly MDBListApiClient _apiClient;
    private readonly ILibraryManager _libraryManager;
    private readonly IUserDataManager _userDataManager;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WatchedSync"/> class.
    /// </summary>
    /// <param name="payloadBuilder">Instance of the <see cref="SyncPayloadBuilder"/>.</param>
    /// <param name="stateStore">Instance of the <see cref="SyncStateStore"/>.</param>
    /// <param name="apiClient">Instance of the <see cref="MDBListApiClient"/>.</param>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="userDataManager">Instance of the <see cref="IUserDataManager"/> interface.</param>
    /// <param name="logManager">Instance of the <see cref="ILogManager"/> interface.</param>
    public WatchedSync(
        SyncPayloadBuilder payloadBuilder,
        SyncStateStore stateStore,
        MDBListApiClient apiClient,
        ILibraryManager libraryManager,
        IUserDataManager userDataManager,
        ILogManager logManager)
    {
        _payloadBuilder = payloadBuilder;
        _stateStore = stateStore;
        _apiClient = apiClient;
        _libraryManager = libraryManager;
        _userDataManager = userDataManager;
        _logger = logManager.GetLogger("MDBList.Sync");
    }

    /// <summary>
    /// Full membership diff against the whole snapshot.
    /// </summary>
    /// <param name="userId">The Emby user.</param>
    /// <param name="accessToken">A valid MDBList access token.</param>
    /// <param name="snapshot">The current library snapshot.</param>
    /// <param name="allowRemovals">See <see cref="SyncPayloadBuilder.DiffAndReconcileAsync"/>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>How many items were pushed as added/removed/skipped.</returns>
    public async Task<PushResult> PushAsync(Guid userId, string accessToken, LibrarySnapshot snapshot, bool allowRemovals, CancellationToken cancellationToken)
    {
        var current = CurrentWatchedItems(snapshot);

        return await _payloadBuilder.DiffAndReconcileAsync(
            userId,
            Category,
            current,
            items => _payloadBuilder.PushItemsAsync(userId, Category, accessToken, Endpoint, FieldName, items, GetWatchedAtValue, cancellationToken),
            items => _payloadBuilder.PushItemsRemoveAsync(userId, Category, accessToken, RemoveEndpoint, items, cancellationToken),
            valueChanged: (known, item) => known.WatchedAt != item.WatchedAt,
            cancellationToken,
            allowRemovals).ConfigureAwait(false);
    }

    /// <summary>
    /// Immediate push for one item, triggered by a live user-data-saved
    /// notification (Emby's native watched toggle, not just our own
    /// pull-applied writes -- those are filtered out by the caller before
    /// this is reached).
    /// </summary>
    /// <param name="userId">The Emby user.</param>
    /// <param name="accessToken">A valid MDBList access token.</param>
    /// <param name="record">The single item's current state.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>What happened: not mappable, no-op, added, or removed.</returns>
    public async Task<PushOutcome> PushSingleAsync(Guid userId, string accessToken, SnapshotItem record, CancellationToken cancellationToken)
    {
        var key = CanonicalKey(record);
        if (key is null)
        {
            return PushOutcome.NotMappable;
        }

        var known = await _stateStore.GetKnownItemsAsync(userId, Category, cancellationToken).ConfigureAwait(false);
        known.TryGetValue(key, out var knownItem);

        if (record.Played)
        {
            var item = BuildKnownItem(record);
            if (knownItem is not null && knownItem.WatchedAt == item.WatchedAt)
            {
                return PushOutcome.NoOp;
            }

            await _payloadBuilder.PushItemsAsync(userId, Category, accessToken, Endpoint, FieldName, [item], GetWatchedAtValue, cancellationToken).ConfigureAwait(false);
            return PushOutcome.Added;
        }

        if (knownItem is null)
        {
            return PushOutcome.NoOp;
        }

        await _payloadBuilder.PushItemsRemoveAsync(userId, Category, accessToken, RemoveEndpoint, [knownItem], cancellationToken).ConfigureAwait(false);
        return PushOutcome.Removed;
    }

    /// <summary>
    /// Pulls remote watched-status changes into Emby -- an incremental
    /// journal read if a cursor exists, otherwise (or if the cursor is
    /// outside the 30-day journal retention window) a full reconciliation.
    /// </summary>
    /// <param name="userId">The Emby user.</param>
    /// <param name="accessToken">A valid MDBList access token.</param>
    /// <param name="user">The resolved Emby user, for writing user data.</param>
    /// <param name="snapshot">The current library snapshot, to match remote entries against.</param>
    /// <param name="serverTime">
    /// /sync/last_activities' own server_time -- a safety-margined
    /// timestamp meant to be persisted as the next watermark, rather than
    /// the device's own clock, which can drift and under-cover the next
    /// incremental window.
    /// </param>
    /// <param name="trusted">
    /// Forwarded to the full-reconcile removal guard -- see
    /// <see cref="ShouldHoldPullRemovals"/> and removal_safety_pattern.md's
    /// Trusted Runs section. Defaults to false so a call site that forgets
    /// to think about it stays safe; only the caller's own trusted-removal
    /// signal (the same one gating push) should pass true.
    /// <see cref="PullIncrementalAsync"/> doesn't need this: it applies
    /// explicit per-item journal events, not a "known minus current-read"
    /// diff, so it isn't the failure mode this pattern guards against.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>How many items were actually changed, and which mode ran.</returns>
    public async Task<PullResult> PullAsync(Guid userId, string accessToken, User user, LibrarySnapshot snapshot, string? serverTime, bool trusted, CancellationToken cancellationToken)
    {
        var since = await _stateStore.GetSyncedAtAsync(userId, Category, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(since))
        {
            _logger.Debug("MDBList Sync: watched pull for user {0} has no cursor - running full pull", user.Name);
            return await PullFullAsync(userId, accessToken, user, snapshot, serverTime, trusted, cancellationToken).ConfigureAwait(false);
        }

        var journal = await _apiClient.FetchJournalAsync(accessToken, since, JournalPageSize, cancellationToken).ConfigureAwait(false);
        if (journal.RequiresFullSync)
        {
            _logger.Debug(
                "MDBList Sync: watched pull cursor {0} for user {1} is outside journal retention - running full pull",
                since,
                user.Name);
            return await PullFullAsync(userId, accessToken, user, snapshot, serverTime, trusted, cancellationToken).ConfigureAwait(false);
        }

        _logger.Debug(
            "MDBList Sync: watched pull cursor {0} for user {1} - running incremental pull ({2} journal entries)",
            since,
            user.Name,
            journal.Entries.Count);
        return await PullIncrementalAsync(userId, user, journal.Entries, snapshot, serverTime, cancellationToken).ConfigureAwait(false);
    }

    private async Task<PullResult> PullFullAsync(Guid userId, string accessToken, User user, LibrarySnapshot snapshot, string? serverTime, bool trusted, CancellationToken cancellationToken)
    {
        // extended=null (full, not ids_only): ids_only only exposes a
        // movie's tmdb id (and an episode's parent show's tmdb id). A local
        // item identified only by imdb/tvdb carries no tmdb id at all, so it
        // could never be matched below with that alone -- full mode gives
        // every provider id.
        var data = await _apiClient.FetchSyncItemsAsync(accessToken, Endpoint, mediatype: null, since: null, extended: null, JournalPageSize, cancellationToken)
            .ConfigureAwait(false);

        _logger.Debug(
            "MDBList Sync: watched full pull for user {0} fetched {1} movies, {2} episodes from MDBList",
            user.Name,
            data.Movies.Count,
            data.Episodes.Count);

        var applied = 0;
        var matchedKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in data.Movies)
        {
            var ids = entry.Movie?.Ids;
            if (ids is null || ids.IsEmpty)
            {
                continue;
            }

            var (appliedOk, key) = ApplyMovieEntry(user, snapshot, ids, "active", entry.LastWatchedAt);
            if (key is not null)
            {
                matchedKeys.Add(key);
            }

            if (appliedOk)
            {
                applied++;
            }
        }

        foreach (var entry in data.Episodes)
        {
            var showIds = entry.Episode?.Show?.Ids;
            if (showIds is null || showIds.IsEmpty)
            {
                continue;
            }

            var (appliedOk, key) = ApplyEpisodeEntry(user, snapshot, showIds, entry.Episode?.Season, entry.Episode?.Number, entry.Episode?.Ids, "active", entry.LastWatchedAt);
            if (key is not null)
            {
                matchedKeys.Add(key);
            }

            if (appliedOk)
            {
                applied++;
            }
        }

        // The full list above is authoritative: anything locally watched but
        // not in it was unwatched remotely -- the fallback for when the
        // journal's 30-day retention window has lapsed, so there's no
        // incremental removal feed to rely on instead.
        //
        // This is the same "known minus current-read = remove" shape
        // DiffAndReconcileAsync guards against on push, just mirrored to the
        // opposite direction: a successful-but-degraded /sync/watched
        // response would otherwise read as "everything was unwatched
        // remotely" and wipe local state. Same three guards (trust,
        // empty-vs-threshold, magnitude), same constants -- see
        // removal_safety_pattern.md.
        var locallyWatched = new List<(SnapshotItem Record, string Key)>();
        foreach (var movie in snapshot.Movies)
        {
            var key = movie.Played ? ItemKeys.CanonicalMovieKey(movie.Ids) : null;
            if (key is not null)
            {
                locallyWatched.Add((movie, key));
            }
        }

        foreach (var episode in snapshot.Episodes)
        {
            var key = episode.Played ? ItemKeys.CanonicalEpisodeKey(episode.Ids, episode.Season, episode.EpisodeNumber) : null;
            if (key is not null)
            {
                locallyWatched.Add((episode, key));
            }
        }

        var candidateRemovals = locallyWatched.Where(w => !matchedKeys.Contains(w.Key)).ToList();
        var holdRemovals = candidateRemovals.Count > 0
            && ShouldHoldPullRemovals(data.Movies.Count + data.Episodes.Count, candidateRemovals.Count, locallyWatched.Count, trusted);

        if (candidateRemovals.Count > 0 && !holdRemovals)
        {
            // The removal timestamp is the server-provided watermark, not
            // "now": if the item was genuinely rewatched between when the
            // server generated this snapshot and now, its local timestamp
            // needs to be newer than server_time (not a later client-side
            // "now") to correctly win the conflict-resolution check in
            // ApplyWatched.
            var removalAt = serverTime ?? NowIso();
            foreach (var (record, _) in candidateRemovals)
            {
                if (ApplyWatched(user, record, "removed", removalAt))
                {
                    applied++;
                }
            }
        }

        if (holdRemovals)
        {
            // Held, not dropped -- don't advance the watermark either, so
            // the next pull retries a full reconcile from scratch (and, per
            // PullAsync, keeps landing back here) instead of downgrading to
            // the incremental journal path and never revisiting these
            // items.
            return new PullResult { PulledApplied = applied, Mode = "full", SkippedRemove = candidateRemovals.Count };
        }

        await _stateStore.SetSyncedAtAsync(userId, Category, serverTime ?? NowIso(), cancellationToken).ConfigureAwait(false);
        return new PullResult { PulledApplied = applied, Mode = "full" };
    }

    /// <summary>
    /// Same shape as <see cref="SyncPayloadBuilder.DiffAndReconcileAsync"/>'s
    /// removal guard, applied to the pull-direction full reconcile: held
    /// when the trigger isn't trusted for removals, when a totally-empty
    /// remote read sits next to a known-watched baseline bigger than the
    /// threshold, or when the removal batch itself is larger than
    /// max(<see cref="SyncPayloadBuilder.RemovalMinBatch"/>, knownCount *
    /// <see cref="SyncPayloadBuilder.RemovalMaxFraction"/>). The empty-read
    /// check is tied to the threshold rather than an absolute veto, same
    /// reasoning as DiffAndReconcileAsync -- a user whose whole watched
    /// library is smaller than the threshold must still be able to clear it
    /// completely on a trusted run.
    /// </summary>
    private bool ShouldHoldPullRemovals(int remoteCount, int candidateCount, int knownCount, bool trusted)
    {
        var threshold = Math.Max(SyncPayloadBuilder.RemovalMinBatch, (int)(knownCount * SyncPayloadBuilder.RemovalMaxFraction));

        if (!trusted)
        {
            _logger.Debug(
                "MDBList Sync: watched pull removal held ({0} items) - this trigger doesn't allow removals",
                candidateCount);
            return true;
        }

        if (remoteCount == 0 && knownCount > threshold)
        {
            _logger.Warn(
                "MDBList Sync: watched pull removal held - remote full list came back empty while {0} items are locally "
                    + "watched (threshold {1}); treating as an unreliable read rather than a real removal",
                knownCount,
                threshold);
            return true;
        }

        if (candidateCount > threshold)
        {
            _logger.Warn(
                "MDBList Sync: watched pull removal held - {0} of {1} locally watched items would be unwatched "
                    + "(threshold {2}); remote read may be incomplete",
                candidateCount,
                knownCount,
                threshold);
            return true;
        }

        return false;
    }

    private async Task<PullResult> PullIncrementalAsync(
        Guid userId,
        User user,
        IReadOnlyCollection<JournalEntry> entries,
        LibrarySnapshot snapshot,
        string? serverTime,
        CancellationToken cancellationToken)
    {
        var applied = 0;
        var skippedType = 0;

        foreach (var entry in entries)
        {
            if (entry.Category != JournalCategory || entry.Ids is null)
            {
                continue;
            }

            // value_at is the actual watched timestamp and is what
            // conflict resolution must compare against -- but it's only
            // ever set on add/active rows; a removal row has no "value" to
            // speak of and only carries action_at. Falling back to
            // action_at there keeps last-write-wins working for removals
            // instead of silently skipping the conflict check -- same bug
            // once found and fixed in both the Kodi addon and the Jellyfin
            // plugin, carried forward here from the start.
            var remoteAt = entry.ValueAt ?? entry.ActionAt;

            if (entry.ItemType == "movie")
            {
                var (appliedOk, _) = ApplyMovieEntry(user, snapshot, entry.Ids, entry.Status, remoteAt);
                if (appliedOk)
                {
                    applied++;
                }
            }
            else if (entry.ItemType == "episode")
            {
                var (appliedOk, _) = ApplyEpisodeEntry(user, snapshot, entry.Ids, entry.Season, entry.Episode, entry.EpisodeIds, entry.Status, remoteAt);
                if (appliedOk)
                {
                    applied++;
                }
            }
            else
            {
                // show/season-level rows have no directly writable Emby field; skipped
                skippedType++;
            }
        }

        if (skippedType > 0)
        {
            _logger.Debug(
                "MDBList Sync: watched incremental pull for user {0} skipped {1} journal entries with unhandled item type",
                user.Name,
                skippedType);
        }

        await _stateStore.SetSyncedAtAsync(userId, Category, serverTime ?? NowIso(), cancellationToken).ConfigureAwait(false);
        return new PullResult { PulledApplied = applied, Mode = "incremental" };
    }

    private (bool Applied, string? Key) ApplyMovieEntry(User user, LibrarySnapshot snapshot, MediaIds ids, string? status, string? remoteAt)
    {
        var match = snapshot.FindMovie(ids);
        if (match is null)
        {
            return (false, null);
        }

        return (ApplyWatched(user, match, status, remoteAt), ItemKeys.CanonicalMovieKey(match.Ids));
    }

    private (bool Applied, string? Key) ApplyEpisodeEntry(User user, LibrarySnapshot snapshot, MediaIds showIds, int? season, int? episode, MediaIds? episodeIds, string? status, string? remoteAt)
    {
        var match = snapshot.FindEpisode(showIds, season, episode, episodeIds);
        if (match is null)
        {
            _logger.Debug(
                "MDBList Sync: watched pull found no local match for show tmdb={0} imdb={1} tvdb={2} S{3}E{4} episodeTmdb={5} episodeTvdb={6}",
                showIds.Tmdb,
                showIds.Imdb,
                showIds.Tvdb,
                season,
                episode,
                episodeIds?.Tmdb,
                episodeIds?.Tvdb);
            return (false, null);
        }

        var key = ItemKeys.CanonicalEpisodeKey(match.Ids, match.Season, match.EpisodeNumber);
        return (ApplyWatched(user, match, status, remoteAt), key);
    }

    /// <summary>
    /// Last-write-wins using Emby's LastPlayedDate vs the remote timestamp.
    /// An exact tie resolves the same way in both branches below -- remote
    /// wins -- one consistent rule rather than local winning on removal but
    /// losing on activation.
    /// </summary>
    private bool ApplyWatched(User user, SnapshotItem record, string? status, string? remoteAt)
    {
        var localTs = record.LastPlayedDate;
        var remoteTs = ParseTimestamp(remoteAt);
        var removed = status == "removed";

        if (!ShouldApplyRemoteWatched(removed, record.PlayCount, localTs, remoteTs))
        {
            return false;
        }

        if (removed)
        {
            SetWatched(user, record.ItemId, played: false, playCount: 0, lastPlayedDate: null);
        }
        else
        {
            SetWatched(user, record.ItemId, played: true, playCount: Math.Max(record.PlayCount, 1), lastPlayedDate: remoteTs ?? record.LastPlayedDate);
        }

        return true;
    }

    /// <summary>
    /// The conflict-resolution decision at the heart of <see cref="ApplyWatched"/>,
    /// pulled out as a pure function so the matrix (local newer / remote
    /// newer / exact tie / missing timestamps, crossed with add vs remove)
    /// is unit-testable without a live <c>IUserDataManager</c>.
    /// </summary>
    /// <param name="removed">Whether the remote row is a removal.</param>
    /// <param name="localPlayCount">The local item's current play count.</param>
    /// <param name="localTs">The local item's <c>LastPlayedDate</c>, if any.</param>
    /// <param name="remoteTs">The remote row's effective timestamp, if any.</param>
    /// <returns>True if the remote state should be applied locally.</returns>
    internal static bool ShouldApplyRemoteWatched(bool removed, int localPlayCount, DateTimeOffset? localTs, DateTimeOffset? remoteTs)
    {
        if (removed)
        {
            if (localPlayCount <= 0)
            {
                return false;
            }

            return !(localTs.HasValue && remoteTs.HasValue && localTs > remoteTs);
        }

        return !(localPlayCount > 0 && localTs.HasValue && remoteTs.HasValue && localTs > remoteTs);
    }

    private void SetWatched(User user, Guid itemId, bool played, int playCount, DateTimeOffset? lastPlayedDate)
    {
        var item = _libraryManager.GetItemById(itemId);
        if (item is null)
        {
            return;
        }

        var userData = _userDataManager.GetUserData(user, item) ?? new UserItemData { Key = item.UserDataKey };
        userData.Played = played;
        userData.PlayCount = playCount;

        // Only overwrite when we have a real value -- on removal this
        // leaves it untouched, matching Emby's own "mark unplayed"
        // behavior rather than forcing an empty/invalid date onto the item.
        if (lastPlayedDate.HasValue)
        {
            userData.LastPlayedDate = lastPlayedDate;
        }

        _userDataManager.SaveUserData(user, item, userData, UserDataSaveReason.Import, CancellationToken.None);
    }

    private static DateTimeOffset? ParseTimestamp(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;
    }

    private static string NowIso()
    {
        return DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
    }

    private static string? CanonicalKey(SnapshotItem record)
    {
        return record.Type == "movie"
            ? ItemKeys.CanonicalMovieKey(record.Ids)
            : ItemKeys.CanonicalEpisodeKey(record.Ids, record.Season, record.EpisodeNumber);
    }

    private static KnownSyncItem BuildKnownItem(SnapshotItem record)
    {
        return new KnownSyncItem
        {
            Type = record.Type,
            Ids = record.Ids,
            Season = record.Season,
            Episode = record.EpisodeNumber,
            WatchedAt = record.LastPlayedDate?.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
        };
    }

    private static JsonNode? GetWatchedAtValue(KnownSyncItem item)
    {
        return item.WatchedAt is null ? null : JsonValue.Create(item.WatchedAt);
    }

    private static Dictionary<string, KnownSyncItem> CurrentWatchedItems(LibrarySnapshot snapshot)
    {
        var items = new Dictionary<string, KnownSyncItem>(StringComparer.Ordinal);

        foreach (var movie in snapshot.Movies)
        {
            if (!movie.Played)
            {
                continue;
            }

            var key = ItemKeys.CanonicalMovieKey(movie.Ids);
            if (key is not null)
            {
                items[key] = BuildKnownItem(movie);
            }
        }

        foreach (var episode in snapshot.Episodes)
        {
            if (!episode.Played)
            {
                continue;
            }

            var key = ItemKeys.CanonicalEpisodeKey(episode.Ids, episode.Season, episode.EpisodeNumber);
            if (key is not null)
            {
                items[key] = BuildKnownItem(episode);
            }
        }

        return items;
    }
}
