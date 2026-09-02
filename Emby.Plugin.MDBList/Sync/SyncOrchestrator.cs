using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emby.Plugin.MDBList.Api;
using Emby.Plugin.MDBList.Api.Models;
using Emby.Plugin.MDBList.Configuration;
using Emby.Plugin.MDBList.Library;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Logging;

namespace Emby.Plugin.MDBList.Sync;

/// <summary>
/// Single-flight coordination plus the periodic full-run and cheap
/// activity-gated pull -- port of jellyfin-plugin-mdblist's
/// SyncOrchestrator.cs.
///
/// Confirmed live in Phase 0 that Emby's own DI container resolves plain,
/// unregistered concrete classes automatically (no IPluginServiceRegistrator
/// equivalent exists or is needed), so this and every other service below
/// it are constructor-injected directly, exactly like the Jellyfin sibling.
/// </summary>
public sealed class SyncOrchestrator : IDisposable
{
    private static readonly string[] WatchedActivityKeys = ["watched_at", "season_watched_at", "episode_watched_at"];
    private static readonly string[] RatingActivityKeys = ["rated_at"];

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IUserManager _userManager;
    private readonly ILibraryManager _libraryManager;
    private readonly IUserDataManager _userDataManager;
    private readonly OAuthService _oauthService;
    private readonly MDBListApiClient _apiClient;
    private readonly SyncStateStore _stateStore;
    private readonly WatchedSync _watchedSync;
    private readonly RatingsSync _ratingsSync;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SyncOrchestrator"/> class.
    /// </summary>
    /// <param name="userManager">Instance of the <see cref="IUserManager"/> interface.</param>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="userDataManager">Instance of the <see cref="IUserDataManager"/> interface.</param>
    /// <param name="oauthService">Instance of the <see cref="OAuthService"/>.</param>
    /// <param name="apiClient">Instance of the <see cref="MDBListApiClient"/>.</param>
    /// <param name="stateStore">Instance of the <see cref="SyncStateStore"/>.</param>
    /// <param name="watchedSync">Instance of the <see cref="WatchedSync"/>.</param>
    /// <param name="ratingsSync">Instance of the <see cref="RatingsSync"/>.</param>
    /// <param name="logManager">Instance of the <see cref="ILogManager"/> interface.</param>
    public SyncOrchestrator(
        IUserManager userManager,
        ILibraryManager libraryManager,
        IUserDataManager userDataManager,
        OAuthService oauthService,
        MDBListApiClient apiClient,
        SyncStateStore stateStore,
        WatchedSync watchedSync,
        RatingsSync ratingsSync,
        ILogManager logManager)
    {
        _userManager = userManager;
        _libraryManager = libraryManager;
        _userDataManager = userDataManager;
        _oauthService = oauthService;
        _apiClient = apiClient;
        _stateStore = stateStore;
        _watchedSync = watchedSync;
        _ratingsSync = ratingsSync;
        _logger = logManager.GetLogger("MDBList.Sync");
    }

    /// <summary>
    /// Attempts to acquire the sync lock without blocking.
    /// </summary>
    /// <returns>
    /// A disposable that releases the lock when acquired; null if a sync is
    /// already in progress. Dispose the non-null result when done -- it
    /// holds the lock for the caller's whole operation, not just the check.
    /// </returns>
    public IDisposable? TryLock()
    {
        return _gate.Wait(0) ? new Releaser(_gate) : null;
    }

    /// <summary>
    /// Full run: rebuilds the library snapshot unconditionally and does
    /// push-then-pull for every enabled category. Safe to call from
    /// multiple trigger points; overlapping calls are skipped rather than
    /// queued.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>True if a run actually executed.</returns>
    public async Task<bool> RunAsync(CancellationToken cancellationToken)
    {
        using var handle = TryLock();
        if (handle is null)
        {
            _logger.Debug("MDBList Sync: run already in progress, skipping");
            return false;
        }

        var linked = ResolveLinkedUser();
        if (linked is null)
        {
            return false;
        }

        var (user, config) = linked.Value;

        var accessToken = await _oauthService.EnsureValidTokenAsync(user.Id, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(accessToken))
        {
            return false;
        }

        try
        {
            // Snapshot build and server_time fetch live inside this try: a
            // failed query must abort the whole run rather than let
            // diff-based reconciliation treat an incomplete snapshot as "the
            // library is empty" and push bulk removals.
            var snapshot = LibrarySnapshot.Build(_libraryManager, _userDataManager, user);
            var activities = await _apiClient.FetchLastActivitiesAsync(accessToken, cancellationToken).ConfigureAwait(false);

            var watchedSummary = "watched skipped";
            if (config.WatchedEnabled)
            {
                var watchedPush = await _watchedSync.PushAsync(user.Id, accessToken, snapshot, cancellationToken).ConfigureAwait(false);
                var watchedPull = await _watchedSync.PullAsync(user.Id, accessToken, user, snapshot, activities.ServerTime, cancellationToken)
                    .ConfigureAwait(false);
                watchedSummary = string.Format(
                    CultureInfo.InvariantCulture,
                    "watched push +{0}/-{1} pull {2} ({3})",
                    watchedPush.PushedAdd,
                    watchedPush.PushedRemove,
                    watchedPull.PulledApplied,
                    watchedPull.Mode);
            }

            var ratingsSummary = "ratings skipped";
            if (config.RatingsEnabled)
            {
                var ratingsPush = await _ratingsSync.PushAsync(user.Id, accessToken, snapshot, cancellationToken).ConfigureAwait(false);
                var ratingsPull = await _ratingsSync.PullAsync(user.Id, accessToken, user, snapshot, activities.ServerTime, cancellationToken)
                    .ConfigureAwait(false);
                ratingsSummary = string.Format(
                    CultureInfo.InvariantCulture,
                    "ratings push +{0}/-{1} pull {2} ({3})",
                    ratingsPush.PushedAdd,
                    ratingsPush.PushedRemove,
                    ratingsPull.PulledApplied,
                    ratingsPull.Mode);
            }

            // Collection sync is wired in once CollectionSync.cs exists.
            var collectionSummary = "collection skipped";

            var summary = $"{watchedSummary}, {ratingsSummary}, {collectionSummary}";
            _logger.Info("MDBList Sync: run complete - {0}", summary);
            await _stateStore.SetLastRunSummaryAsync(user.Id, summary, cancellationToken).ConfigureAwait(false);

            return true;
        }
        catch (MDBListApiException ex)
        {
            _logger.ErrorException("MDBList Sync: run failed", ex);
            await _stateStore.SetLastRunSummaryAsync(user.Id, $"run failed: {ex.Message}", cancellationToken).ConfigureAwait(false);
            return false;
        }
    }

    /// <summary>
    /// Cheap poll: checks /sync/last_activities (a single lightweight GET)
    /// and only pays for a library snapshot rebuild + pull when a relevant
    /// bucket actually advanced since the last check.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>True if a pull actually ran.</returns>
    public async Task<bool> CheckActivityAsync(CancellationToken cancellationToken)
    {
        using var handle = TryLock();
        if (handle is null)
        {
            _logger.Debug("MDBList Sync: activity check skipped, a run is already in progress");
            return false;
        }

        var linked = ResolveLinkedUser();
        if (linked is null)
        {
            return false;
        }

        var (user, config) = linked.Value;

        var accessToken = await _oauthService.EnsureValidTokenAsync(user.Id, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(accessToken))
        {
            return false;
        }

        LastActivities activities;
        try
        {
            activities = await _apiClient.FetchLastActivitiesAsync(accessToken, cancellationToken).ConfigureAwait(false);
        }
        catch (MDBListApiException ex)
        {
            _logger.Debug("MDBList Sync: activity check failed: {0}", ex.Message);
            return false;
        }

        var seen = await _stateStore.GetLastActivitiesSeenAsync(user.Id, cancellationToken).ConfigureAwait(false);
        var current = ToDictionary(activities);

        // journal_at covers removals (it doesn't say which category) --
        // confirmed against api.mdblist's removal endpoints, which only
        // clear per-item state and separately bump journal_at. Without
        // checking it too, an unwatch/unrate never trips this gate.
        var journalAdvanced = AnyBucketAdvanced(seen, current, "journal_at");
        var watchedChanged = config.WatchedEnabled && (journalAdvanced || AnyBucketAdvanced(seen, current, WatchedActivityKeys));
        var ratingsChanged = config.RatingsEnabled && (journalAdvanced || AnyBucketAdvanced(seen, current, RatingActivityKeys));

        if (!watchedChanged && !ratingsChanged)
        {
            // Advance the watermark only when there's nothing to follow up
            // on. If a pull below fails, the watermark must stay put so
            // this gets retried on the next check instead of silently
            // marked "seen" -- see the matching comment further down.
            await _stateStore.SetLastActivitiesSeenAsync(user.Id, current, cancellationToken).ConfigureAwait(false);
            _logger.Debug("MDBList Sync: activity check found nothing new");
            return false;
        }

        var snapshot = LibrarySnapshot.Build(_libraryManager, _userDataManager, user);
        var summaries = new List<string>();

        try
        {
            if (watchedChanged)
            {
                var watchedPull = await _watchedSync.PullAsync(user.Id, accessToken, user, snapshot, activities.ServerTime, cancellationToken)
                    .ConfigureAwait(false);
                summaries.Add(string.Format(CultureInfo.InvariantCulture, "watched pull {0} ({1})", watchedPull.PulledApplied, watchedPull.Mode));
            }

            if (ratingsChanged)
            {
                var ratingsPull = await _ratingsSync.PullAsync(user.Id, accessToken, user, snapshot, activities.ServerTime, cancellationToken)
                    .ConfigureAwait(false);
                summaries.Add(string.Format(CultureInfo.InvariantCulture, "ratings pull {0} ({1})", ratingsPull.PulledApplied, ratingsPull.Mode));
            }
        }
        catch (MDBListApiException ex)
        {
            _logger.ErrorException("MDBList Sync: activity-triggered pull failed", ex);
            return false;
        }

        var summary = "activity check: " + string.Join(", ", summaries);
        _logger.Info("MDBList Sync: {0}", summary);
        await _stateStore.SetLastRunSummaryAsync(user.Id, summary, cancellationToken).ConfigureAwait(false);

        // Only reached on success, so a failed pull above leaves the
        // watermark where it was.
        await _stateStore.SetLastActivitiesSeenAsync(user.Id, current, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _gate.Dispose();
    }

    private (User User, UserSyncConfig Config)? ResolveLinkedUser()
    {
        var linkedUserConfig = Plugin.Instance?.Configuration.Users.FirstOrDefault();
        if (linkedUserConfig is null)
        {
            return null;
        }

        var user = _userManager.GetUserById(linkedUserConfig.EmbyUserId);
        return user is null ? null : (user, linkedUserConfig);
    }

    private static bool AnyBucketAdvanced(Dictionary<string, string> seen, Dictionary<string, string> current, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (current.TryGetValue(key, out var currentValue)
                && !string.IsNullOrEmpty(currentValue)
                && (!seen.TryGetValue(key, out var seenValue) || seenValue != currentValue))
            {
                return true;
            }
        }

        return false;
    }

    private static Dictionary<string, string> ToDictionary(LastActivities activities)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        AddIfPresent(dict, "watchlisted_at", activities.WatchlistedAt);
        AddIfPresent(dict, "watched_at", activities.WatchedAt);
        AddIfPresent(dict, "season_watched_at", activities.SeasonWatchedAt);
        AddIfPresent(dict, "episode_watched_at", activities.EpisodeWatchedAt);
        AddIfPresent(dict, "rated_at", activities.RatedAt);
        AddIfPresent(dict, "journal_at", activities.JournalAt);
        AddIfPresent(dict, "collected_at", activities.CollectedAt);
        AddIfPresent(dict, "dropped_at", activities.DroppedAt);
        AddIfPresent(dict, "server_time", activities.ServerTime);
        return dict;
    }

    private static void AddIfPresent(Dictionary<string, string> dict, string key, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            dict[key] = value;
        }
    }

    private sealed class Releaser : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;

        public Releaser(SemaphoreSlim semaphore)
        {
            _semaphore = semaphore;
        }

        public void Dispose()
        {
            _semaphore.Release();
        }
    }
}
