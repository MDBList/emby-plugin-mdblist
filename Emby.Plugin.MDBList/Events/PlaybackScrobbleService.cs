using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Emby.Plugin.MDBList.Api;
using Emby.Plugin.MDBList.Api.Models;
using Emby.Plugin.MDBList.Library;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Logging;

namespace Emby.Plugin.MDBList.Events;

/// <summary>
/// Live playback progress pushed to MDBList's /scrobble/* endpoints -- port
/// of jellyfin-plugin-mdblist's PlaybackScrobbleService.cs (itself
/// player_monitor.py). One-way and independent of watched-status sync: the
/// server uses this feed for its own watched-marking (at >=80% progress)
/// and "continue watching" position, so this never writes back to Emby and
/// needs none of WatchedSync's echo-loop guards.
///
/// <see cref="ISessionManager.PlaybackStart"/>/<c>PlaybackProgress</c>/
/// <c>PlaybackStopped</c> and their event arg shapes (<c>Item</c>,
/// <c>Users</c>, <c>PlaybackPositionTicks</c>, <c>IsPaused</c>,
/// <c>PlaySessionId</c>, <c>Session</c>, <c>PlayedToCompletion</c>) are
/// confirmed identical to Jellyfin's by reflection, so this logic ports
/// close to verbatim -- only the DI/logging plumbing changes.
/// </summary>
public sealed class PlaybackScrobbleService
{
    private const string StartEndpoint = "/scrobble/start";
    private const string PauseEndpoint = "/scrobble/pause";
    private const string StopEndpoint = "/scrobble/stop";

    // Progress ticks arrive about every second; a start already carries the
    // progress and MDBList runs the session on from it, so periodic pushes
    // only matter when the server never sees a stop (crash, killed client).
    // Pause, resume, seek and stop are still sent right away.
    private static readonly TimeSpan MinProgressInterval = TimeSpan.FromMinutes(15);

    // A tick whose position is further than this from where steady playback
    // would have put it is a seek, sent right away: MDBList runs progress on
    // from the last push, so it would be off until the next one otherwise.
    private static readonly TimeSpan SeekTolerance = TimeSpan.FromSeconds(30);

    // How long periodic progress pushes pause for a user after a 429 that
    // carries no Retry-After.
    private static readonly TimeSpan RateLimitPause = TimeSpan.FromMinutes(10);

    private readonly OAuthService _oauthService;
    private readonly MDBListApiClient _apiClient;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, SessionState> _sessions = new(StringComparer.Ordinal);

    // Per user: periodic progress pushes are skipped until this time after a
    // 429, rather than spending more of an exhausted quota every tick.
    private readonly ConcurrentDictionary<Guid, DateTime> _pausedUntil = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaybackScrobbleService"/> class.
    /// </summary>
    /// <param name="oauthService">Instance of the <see cref="OAuthService"/>.</param>
    /// <param name="apiClient">Instance of the <see cref="MDBListApiClient"/>.</param>
    /// <param name="logManager">Instance of the <see cref="ILogManager"/> interface.</param>
    public PlaybackScrobbleService(OAuthService oauthService, MDBListApiClient apiClient, ILogManager logManager)
    {
        _oauthService = oauthService;
        _apiClient = apiClient;
        _logger = logManager.GetLogger("MDBList.Scrobble");
    }

    /// <summary>
    /// Handles <c>ISessionManager.PlaybackStart</c>.
    /// </summary>
    /// <param name="e">The event args.</param>
    public void HandlePlaybackStart(PlaybackProgressEventArgs e)
    {
        if (!IsScrobblable(e.Item))
        {
            return;
        }

        var key = SessionKey(e);
        if (key is null)
        {
            return;
        }

        // Clients starting from the beginning report no position: that's 0.
        // Left null, no progress could be computed, the start wasn't sent, and
        // the throttle then held every push back for MinProgressInterval.
        var positionTicks = e.PlaybackPositionTicks ?? 0;

        // Recorded synchronously, before the send below even runs: an
        // automated progress tick can arrive very soon after, and it must
        // see "just sent" immediately, not race the async send.
        var state = new SessionState { LastSentAt = DateTime.UtcNow, LastSentPositionTicks = positionTicks };
        _sessions[key] = state;
        Dispatch(e, StartEndpoint, playedToCompletion: false, state, positionTicks: positionTicks);
    }

    /// <summary>
    /// Handles <c>ISessionManager.PlaybackProgress</c> -- covers pause,
    /// resume, seek, and periodic progress ticks alike.
    /// </summary>
    /// <param name="e">The event args.</param>
    public void HandlePlaybackProgress(PlaybackProgressEventArgs e)
    {
        if (!IsScrobblable(e.Item))
        {
            return;
        }

        var key = SessionKey(e);
        if (key is null)
        {
            return;
        }

        var state = _sessions.GetOrAdd(key, static _ => new SessionState());

        if (e.IsPaused)
        {
            if (state.WasPaused)
            {
                return;
            }

            state.WasPaused = true;
            Dispatch(e, PauseEndpoint, playedToCompletion: false);
            return;
        }

        var resuming = state.WasPaused;
        state.WasPaused = false;

        // MDBList answered 404 for this title: re-sending it every tick
        // won't change that before the playback ends
        if (!resuming && state.TitleUnknown)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var seeking = !resuming && IsSeek(state.LastSentPositionTicks, state.LastSentAt, e.PlaybackPositionTicks, now);
        if (!resuming && !seeking && now - state.LastSentAt < MinProgressInterval)
        {
            return;
        }

        state.LastSentAt = now;
        state.LastSentPositionTicks = e.PlaybackPositionTicks;
        Dispatch(e, StartEndpoint, playedToCompletion: false, state, periodic: !resuming && !seeking);
    }

    /// <summary>
    /// Handles <c>ISessionManager.PlaybackStopped</c> -- covers both a
    /// genuine stop and playing through to the end.
    /// </summary>
    /// <param name="e">The event args.</param>
    public void HandlePlaybackStopped(PlaybackStopEventArgs e)
    {
        // Also keeps a pre-roll's stop, which can arrive after the feature has
        // started on the same session, from dropping the feature's state
        if (!IsScrobblable(e.Item))
        {
            return;
        }

        var key = SessionKey(e);
        if (key is not null)
        {
            _sessions.TryRemove(key, out _);
        }

        Dispatch(e, StopEndpoint, e.PlayedToCompletion);
    }

    private static string? SessionKey(PlaybackProgressEventArgs e)
    {
        return e.PlaySessionId ?? e.Session?.Id;
    }

    // positionTicks: the position to send, when not the event's own
    private void Dispatch(PlaybackProgressEventArgs e, string endpoint, bool playedToCompletion, SessionState? state = null, bool periodic = false, long? positionTicks = null)
    {
        _ = Task.Run(async () =>
        {
            // Fire and forget: anything thrown here would otherwise be lost
            // without a trace (API errors are already handled per user below)
            try
            {
                await SendAsync(e, endpoint, playedToCompletion, state, periodic, positionTicks ?? e.PlaybackPositionTicks).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.ErrorException("MDBList scrobble {0} for {1} failed", ex, endpoint, e.Item?.Name);
            }
        });
    }

    private async Task SendAsync(PlaybackProgressEventArgs e, string endpoint, bool playedToCompletion, SessionState? state, bool periodic, long? positionTicks)
    {
        var userIds = ResolveScrobblingLinkedUserIds(e.Users);
        if (periodic)
        {
            var now = DateTime.UtcNow;
            userIds.RemoveAll(id => _pausedUntil.TryGetValue(id, out var until) && now < until);
        }

        if (userIds.Count == 0)
        {
            return;
        }

        var request = BuildScrobbleRequest(e.Item, positionTicks, playedToCompletion);
        if (request is null)
        {
            return;
        }

        // A session usually has exactly one user, but Emby models it as a
        // list (e.g. profile-sharing setups) -- push once per linked user
        // actually on this session, not just the first one found overall.
        foreach (var userId in userIds)
        {
            await SendForUserAsync(userId, endpoint, request, state, e.Item?.Name).ConfigureAwait(false);
        }
    }

    private async Task SendForUserAsync(Guid userId, string endpoint, ScrobbleRequest request, SessionState? state, string? itemName)
    {
        try
        {
            var accessToken = await _oauthService.EnsureValidTokenAsync(userId, CancellationToken.None).ConfigureAwait(false);
            if (string.IsNullOrEmpty(accessToken))
            {
                return;
            }

            // Only a stop is worth waiting out a 429 for (it can be what
            // marks the title watched); a start/pause would land stale, and
            // possibly after a later stop
            await _apiClient.PushSyncItemsAsync(accessToken, endpoint, request, CancellationToken.None, retryRateLimit: endpoint == StopEndpoint).ConfigureAwait(false);
            _logger.Debug("MDBList scrobble {0} for {1} at {2}% sent for user {3}", endpoint, itemName, request.Progress, userId);
        }
        catch (MDBListApiException ex)
        {
            if (ex.IsRateLimited)
            {
                _pausedUntil[userId] = DateTime.UtcNow + (ex.RetryAfter ?? RateLimitPause);
            }
            else if (ex.StatusCode == HttpStatusCode.NotFound && endpoint == StartEndpoint && state is not null)
            {
                state.TitleUnknown = true;
            }

            // A 429 or a title MDBList doesn't know is expected and handled
            // here; anything else is a real failure, worth a warning
            if (ex.IsRateLimited || ex.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.Debug("MDBList scrobble {0} for {1} failed for user {2}: {3}", endpoint, itemName, userId, ex.Message);
            }
            else
            {
                _logger.Warn("MDBList scrobble {0} for {1} failed for user {2}: {3}", endpoint, itemName, userId, ex.Message);
            }
        }
        catch (Exception ex) when (ex is not MDBListApiException)
        {
            // E.g. an HttpClient timeout or a failed token refresh: caught per
            // user so the session's other users still get this scrobble
            _logger.Warn("MDBList scrobble {0} for {1} failed for user {2}: {3}", endpoint, itemName, userId, ex.Message);
        }
    }

    private static List<Guid> ResolveScrobblingLinkedUserIds(IReadOnlyList<User> sessionUsers)
    {
        var linkedUsers = Plugin.Instance?.Configuration.Users;
        if (linkedUsers is null || linkedUsers.Count == 0)
        {
            return [];
        }

        var result = new List<Guid>();
        foreach (var sessionUser in sessionUsers)
        {
            var config = linkedUsers.FirstOrDefault(u => u.EmbyUserId == sessionUser.Id);
            if (config is not null && config.ScrobblingEnabled)
            {
                result.Add(sessionUser.Id);
            }
        }

        return result;
    }

    private static ScrobbleRequest? BuildScrobbleRequest(BaseItem? item, long? positionTicks, bool playedToCompletion)
    {
        if (item is null)
        {
            return null;
        }

        var progress = playedToCompletion ? 100.0 : ComputeProgress(item.RunTimeTicks, positionTicks);
        if (progress is null)
        {
            return null;
        }

        var appVersion = Plugin.Instance?.Version.ToString();

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
            return new ScrobbleRequest
            {
                Show = new ScrobbleShowRef
                {
                    Ids = showIds,
                    Season = new ScrobbleSeasonRef
                    {
                        Number = episode.ParentIndexNumber,
                        Episode = new ScrobbleEpisodeRef
                        {
                            Number = episode.IndexNumber,
                            Ids = episodeIds.IsEmpty ? null : episodeIds,
                        },
                    },
                },
                Progress = progress.Value,
                AppVersion = appVersion,
            };
        }

        if (item is Movie)
        {
            var ids = MediaIdMapper.MapMovieIds(item.ProviderIds);
            if (ids.IsEmpty)
            {
                return null;
            }

            return new ScrobbleRequest
            {
                Movie = new ScrobbleMovieRef { Ids = ids },
                Progress = progress.Value,
                AppVersion = appVersion,
            };
        }

        return null;
    }

    /// <summary>
    /// Whether a progress tick's position is off from where playback since
    /// the last push would have put it by more than <see cref="SeekTolerance"/>.
    /// </summary>
    /// <param name="lastPositionTicks">The position last pushed.</param>
    /// <param name="lastSentAt">When it was pushed.</param>
    /// <param name="positionTicks">The tick's position.</param>
    /// <param name="now">The tick's time.</param>
    /// <returns>True when the tick is a seek.</returns>
    internal static bool IsSeek(long? lastPositionTicks, DateTime lastSentAt, long? positionTicks, DateTime now)
    {
        if (lastPositionTicks is null || positionTicks is null)
        {
            return false;
        }

        var expected = lastPositionTicks.Value + (now - lastSentAt).Ticks;
        return Math.Abs(positionTicks.Value - expected) > SeekTolerance.Ticks;
    }

    /// <summary>
    /// Whether a playback is one MDBList can scrobble: a movie or episode,
    /// not an extra (trailer, clip, ...) or a pre-roll (cinema mode intros
    /// play as plain videos or trailers). Others are ignored before they
    /// touch session state.
    /// </summary>
    /// <param name="item">The item being played.</param>
    /// <returns>True for a movie or episode that isn't an extra.</returns>
    internal static bool IsScrobblable(BaseItem? item)
    {
        return item is Movie or Episode && item.ExtraType is null;
    }

    internal static double? ComputeProgress(long? runTimeTicks, long? positionTicks)
    {
        if (runTimeTicks is null or <= 0 || positionTicks is null)
        {
            return null;
        }

        var clamped = Math.Clamp(positionTicks.Value, 0, runTimeTicks.Value);
        return Math.Round((double)clamped / runTimeTicks.Value * 100, 2);
    }

    private sealed class SessionState
    {
        public bool WasPaused { get; set; }

        public bool TitleUnknown { get; set; }

        public DateTime LastSentAt { get; set; } = DateTime.MinValue;

        public long? LastSentPositionTicks { get; set; }
    }
}
