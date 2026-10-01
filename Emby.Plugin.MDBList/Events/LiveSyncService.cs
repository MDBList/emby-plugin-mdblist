using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emby.Plugin.MDBList.Api;
using Emby.Plugin.MDBList.Library;
using Emby.Plugin.MDBList.Sync;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;

namespace Emby.Plugin.MDBList.Events;

/// <summary>
/// Live single-item push triggered by a user-data-saved notification --
/// port of jellyfin-plugin-mdblist's LiveSyncService.cs (itself
/// live_sync.py's handle_library_update).
///
/// Emby's <c>UserDataSaveEventArgs</c> exposes the full <c>User</c> object
/// directly (confirmed by reflection), not just a user id like Jellyfin's
/// version -- simpler here, no extra lookup needed. Emby's
/// <c>UserDataSaveReason</c> enum also has no <c>UpdateUserData</c> member
/// (confirmed by reflection) -- Jellyfin uses that to distinguish a
/// genuine numeric rating (e.g. from Infuse) from the stock web UI's
/// thumbs button (always <c>UpdateUserRating</c>). Without that second
/// reason to fall back on, <c>UpdateUserRating</c> is the only rating
/// trigger available here, so the "ignore thumbs ratings" toggle
/// necessarily suppresses every rating save on this platform, not just the
/// thumbs-originated ones -- a real, documented behavior difference from
/// the Jellyfin sibling, not an oversight.
/// </summary>
public class LiveSyncService
{
    private static readonly HashSet<UserDataSaveReason> WatchedTriggerReasons =
    [
        UserDataSaveReason.TogglePlayed,
        UserDataSaveReason.PlaybackFinished,
    ];

    private static readonly HashSet<UserDataSaveReason> RatingTriggerReasons =
    [
        UserDataSaveReason.UpdateUserRating,
    ];

    // Longer than any sync should take -- a push still waiting after this is
    // left to the next full run
    private static readonly TimeSpan LockWaitTimeout = TimeSpan.FromMinutes(30);

    private readonly IUserDataManager _userDataManager;
    private readonly OAuthService _oauthService;
    private readonly WatchedSync _watchedSync;
    private readonly RatingsSync _ratingsSync;
    private readonly SyncOrchestrator _orchestrator;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LiveSyncService"/> class.
    /// </summary>
    /// <param name="userDataManager">Instance of the <see cref="IUserDataManager"/> interface.</param>
    /// <param name="oauthService">Instance of the <see cref="OAuthService"/>.</param>
    /// <param name="watchedSync">Instance of the <see cref="WatchedSync"/>.</param>
    /// <param name="ratingsSync">Instance of the <see cref="RatingsSync"/>.</param>
    /// <param name="orchestrator">Instance of the <see cref="SyncOrchestrator"/>.</param>
    /// <param name="logManager">Instance of the <see cref="ILogManager"/> interface.</param>
    public LiveSyncService(IUserDataManager userDataManager, OAuthService oauthService, WatchedSync watchedSync, RatingsSync ratingsSync, SyncOrchestrator orchestrator, ILogManager logManager)
    {
        _userDataManager = userDataManager;
        _oauthService = oauthService;
        _watchedSync = watchedSync;
        _ratingsSync = ratingsSync;
        _orchestrator = orchestrator;
        _logger = logManager.GetLogger("MDBList.LiveSync");
    }

    /// <summary>
    /// Entry point from <see cref="MDBListEntryPoint"/>. Filters down to
    /// relevant events synchronously, then hands off to a background task --
    /// the save event fires synchronously on the caller's own thread, so
    /// awaiting an HTTP push here would block it.
    /// </summary>
    /// <param name="e">The event args.</param>
    public void HandleUserDataSaved(UserDataSaveEventArgs e)
    {
        if (e.SaveReason == UserDataSaveReason.Import)
        {
            // Our own pull-applied write -- ignoring it here is one of
            // three independent echo-loop guards (the others: writing
            // pulled state with reason Import in the first place, and
            // holding the orchestrator lock for the whole live push).
            return;
        }

        if (e.Item is not (Movie or Episode))
        {
            return;
        }

        var linkedUserConfig = Plugin.Instance?.Configuration.Users.FirstOrDefault(u => u.EmbyUserId == e.User.Id);
        if (linkedUserConfig is null)
        {
            return;
        }

        var pushWatched = linkedUserConfig.WatchedEnabled && WatchedTriggerReasons.Contains(e.SaveReason);
        var pushRating = linkedUserConfig.RatingsEnabled && RatingTriggerReasons.Contains(e.SaveReason);

        if (pushRating && e.SaveReason == UserDataSaveReason.UpdateUserRating && linkedUserConfig.IgnoreThumbRatings)
        {
            pushRating = false;
        }

        if (!pushWatched && !pushRating)
        {
            return;
        }

        _ = Task.Run(() => HandleAsync(e.User, e.Item, e.UserData, pushWatched, pushRating));
    }

    private async Task HandleAsync(User user, BaseItem item, UserItemData userData, bool pushWatched, bool pushRating)
    {
        var userId = user.Id;
        var handle = _orchestrator.TryLock();
        if (handle is null)
        {
            // A sync (or another live push) holds the lock. Wait for it rather
            // than drop this push: the pull's own writes never get here (saved
            // with reason Import, filtered above), so this is a real user
            // change. The lock is held for the whole block below, not just
            // checked once, so a pull starting mid-handler can't race it.
            try
            {
                handle = await _orchestrator.WaitLockAsync(LockWaitTimeout).ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            if (handle is null)
            {
                _logger.Debug("MDBList live push for {0} dropped - sync still running", item.Name);
                return;
            }

            // The item may have changed again while waiting (e.g. toggled
            // twice) -- push its current state, not this event's
            userData = _userDataManager.GetUserData(user, item) ?? userData;
        }

        using var heldLock = handle;
        try
        {
            var record = BuildRecord(item, userData);
            if (record is null)
            {
                return;
            }

            var accessToken = await _oauthService.EnsureValidTokenAsync(userId, CancellationToken.None).ConfigureAwait(false);
            if (string.IsNullOrEmpty(accessToken))
            {
                return;
            }

            if (pushWatched)
            {
                await _watchedSync.PushSingleAsync(userId, accessToken, record, CancellationToken.None).ConfigureAwait(false);
            }

            if (pushRating)
            {
                await _ratingsSync.PushSingleAsync(userId, accessToken, record, CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (MDBListApiException ex)
        {
            _logger.Debug("MDBList live push failed: {0}", ex.Message);
        }
    }

    private static SnapshotItem? BuildRecord(BaseItem item, UserItemData userData)
    {
        if (item is Episode episode)
        {
            if (episode.Series is null || episode.ParentIndexNumber is null || episode.IndexNumber is null)
            {
                return null;
            }

            var showIds = MediaIdMapper.MapShowIds(episode.Series.ProviderIds);
            if (showIds.IsEmpty)
            {
                return null;
            }

            var episodeIds = MediaIdMapper.MapShowIds(episode.ProviderIds);
            return new SnapshotItem
            {
                Type = "episode",
                ItemId = episode.Id,
                Title = episode.Name,
                Ids = showIds,
                EpisodeIds = episodeIds.IsEmpty ? null : episodeIds,
                Season = episode.ParentIndexNumber,
                EpisodeNumber = episode.IndexNumber,
                Played = userData.Played,
                PlayCount = userData.PlayCount,
                LastPlayedDate = userData.LastPlayedDate,
                Rating = userData.Rating,
                DateCreated = episode.DateCreated,
            };
        }

        var ids = MediaIdMapper.MapMovieIds(item.ProviderIds);
        if (ids.IsEmpty)
        {
            return null;
        }

        return new SnapshotItem
        {
            Type = "movie",
            ItemId = item.Id,
            Title = item.Name,
            Ids = ids,
            Played = userData.Played,
            PlayCount = userData.PlayCount,
            LastPlayedDate = userData.LastPlayedDate,
            Rating = userData.Rating,
            DateCreated = item.DateCreated,
        };
    }
}
