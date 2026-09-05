using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;

namespace Emby.Plugin.MDBList.Sync;

/// <summary>
/// Persisted sync state (cursors + known-items maps), one file per plugin
/// install covering every linked user -- port of jellyfin-plugin-mdblist's
/// SyncStateStore.cs (itself sync_state.py).
///
/// Lives at <c>PluginConfigurationsPath/MDBList/sync_state.json</c> -- same
/// property name confirmed on Emby's own <see cref="IApplicationPaths"/> by
/// reflection.
///
/// Unlike the Jellyfin sibling, this class cannot rely on an in-memory
/// cache surviving across every caller: Emby has no equivalent of
/// Jellyfin's <c>AddSingleton</c> plugin-service registration, so Emby's
/// plugin loader hands the scheduled tasks, the API service, and the
/// config page each their own independently-constructed instance (each
/// with its own empty cache). A cache here would let one instance's stale
/// view -- e.g. the config page's, loaded once before any sync had ever
/// run -- permanently shadow state written by another instance, which is
/// exactly what made "Last run" never update after a scheduled sync (see
/// GitHub issue #1). So every read/mutate below goes straight to disk,
/// which is the only state genuinely shared across instances. Still
/// written atomically (temp file + move) for crash safety.
/// </summary>
public sealed class SyncStateStore : IDisposable
{
    private const string FileName = "sync_state.json";

    // Every collection/dictionary in SyncStateFile's object graph is exposed
    // get-only to satisfy CA2227/CA1002 -- but System.Text.Json does NOT
    // populate a get-only collection property by default (unlike
    // XmlSerializer's population-via-getter behavior used elsewhere in this
    // plugin for PluginConfiguration). Populate opts into that -- see the
    // same fix in jellyfin-plugin-mdblist, confirmed empirically there and
    // carried forward here rather than re-discovering it.
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PreferredObjectCreationHandling = JsonObjectCreationHandling.Populate,
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="SyncStateStore"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    public SyncStateStore(IApplicationPaths applicationPaths)
    {
        _filePath = Path.Combine(applicationPaths.PluginConfigurationsPath, "MDBList", FileName);
    }

    /// <summary>
    /// Gets the incremental-pull cursor for a category. Null means no pull
    /// has ever succeeded, so the next pull must be a full one.
    /// </summary>
    /// <param name="userId">The Emby user.</param>
    /// <param name="category">The sync category.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The cursor, or null.</returns>
    public async Task<string?> GetSyncedAtAsync(Guid userId, SyncCategory category, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var file = await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            return TryGetUserState(file, userId, out var userState) ? GetCategoryState(userState, category).SyncedAt : null;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Sets the incremental-pull cursor for a category.
    /// </summary>
    /// <param name="userId">The Emby user.</param>
    /// <param name="category">The sync category.</param>
    /// <param name="timestamp">The new cursor value.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task SetSyncedAtAsync(Guid userId, SyncCategory category, string timestamp, CancellationToken cancellationToken)
    {
        await MutateAsync(
            file => GetCategoryState(GetOrCreateUserState(file, userId), category).SyncedAt = timestamp,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the last-pushed identity + value for every known item in a category.
    /// </summary>
    /// <param name="userId">The Emby user.</param>
    /// <param name="category">The sync category.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A copy of the known-items map.</returns>
    public async Task<Dictionary<string, KnownSyncItem>> GetKnownItemsAsync(Guid userId, SyncCategory category, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var file = await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            if (!TryGetUserState(file, userId, out var userState))
            {
                return new Dictionary<string, KnownSyncItem>(StringComparer.Ordinal);
            }

            return new Dictionary<string, KnownSyncItem>(GetCategoryState(userState, category).KnownItems, StringComparer.Ordinal);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Merges a batch of upserts and removals into a category's known-items
    /// map in one disk round trip -- used to persist push progress
    /// chunk-by-chunk rather than only once at the very end of a whole
    /// category's diff. That way, if a later chunk aborts the run (e.g. a
    /// rate limit that survives its retry budget), the chunks that already
    /// pushed successfully are not forgotten and re-pushed from scratch on
    /// the next attempt.
    /// </summary>
    /// <param name="userId">The Emby user.</param>
    /// <param name="category">The sync category.</param>
    /// <param name="upserts">Items to add or update, keyed by canonical id.</param>
    /// <param name="removedKeys">Canonical ids to remove.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task MergeKnownItemsAsync(
        Guid userId,
        SyncCategory category,
        IReadOnlyDictionary<string, KnownSyncItem> upserts,
        IReadOnlyCollection<string> removedKeys,
        CancellationToken cancellationToken)
    {
        await MutateAsync(
            file =>
            {
                var state = GetCategoryState(GetOrCreateUserState(file, userId), category);
                foreach (var (key, value) in upserts)
                {
                    state.KnownItems[key] = value;
                }

                foreach (var key in removedKeys)
                {
                    state.KnownItems.Remove(key);
                }
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the last /sync/last_activities snapshot checked for this user.
    /// </summary>
    /// <param name="userId">The Emby user.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A copy of the last-seen activity buckets.</returns>
    public async Task<Dictionary<string, string>> GetLastActivitiesSeenAsync(Guid userId, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var file = await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            if (!TryGetUserState(file, userId, out var userState))
            {
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }

            return new Dictionary<string, string>(userState.LastActivitiesSeen, StringComparer.Ordinal);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Sets the last /sync/last_activities snapshot checked for this user.
    /// </summary>
    /// <param name="userId">The Emby user.</param>
    /// <param name="activities">The activity buckets to record as seen.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task SetLastActivitiesSeenAsync(Guid userId, IReadOnlyDictionary<string, string> activities, CancellationToken cancellationToken)
    {
        await MutateAsync(
            file =>
            {
                var userState = GetOrCreateUserState(file, userId);
                userState.LastActivitiesSeen.Clear();
                foreach (var (key, value) in activities)
                {
                    userState.LastActivitiesSeen[key] = value;
                }
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the most recent sync run's summary text for the config page.
    /// </summary>
    /// <param name="userId">The Emby user.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The summary, or null if no run has completed yet.</returns>
    public async Task<string?> GetLastRunSummaryAsync(Guid userId, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var file = await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            return TryGetUserState(file, userId, out var userState) ? userState.LastRunSummary : null;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Records the most recent sync run's summary text.
    /// </summary>
    /// <param name="userId">The Emby user.</param>
    /// <param name="summary">The summary text.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task SetLastRunSummaryAsync(Guid userId, string summary, CancellationToken cancellationToken)
    {
        await MutateAsync(
            file => GetOrCreateUserState(file, userId).LastRunSummary = summary,
            cancellationToken).ConfigureAwait(false);
    }

    private static CategoryState GetCategoryState(UserSyncState userState, SyncCategory category)
    {
        return category switch
        {
            SyncCategory.Watched => userState.Watched,
            SyncCategory.Ratings => userState.Ratings,
            SyncCategory.Collection => userState.Collection,
            _ => throw new ArgumentOutOfRangeException(nameof(category)),
        };
    }

    private static bool TryGetUserState(SyncStateFile file, Guid userId, out UserSyncState userState)
    {
        return file.Users.TryGetValue(userId.ToString(), out userState!);
    }

    private static UserSyncState GetOrCreateUserState(SyncStateFile file, Guid userId)
    {
        var key = userId.ToString();
        if (!file.Users.TryGetValue(key, out var userState))
        {
            userState = new UserSyncState();
            file.Users[key] = userState;
        }

        return userState;
    }

    private async Task MutateAsync(Action<SyncStateFile> mutate, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var file = await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            mutate(file);
            await WriteToDiskAsync(file, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<SyncStateFile> EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        return await LoadFromDiskAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<SyncStateFile> LoadFromDiskAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var stream = File.OpenRead(_filePath);
            var loaded = await JsonSerializer.DeserializeAsync<SyncStateFile>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false);
            return loaded ?? new SyncStateFile();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // No file yet (first run) or an unreadable one -- start fresh
            // rather than fail the whole plugin; a corrupt state file forces
            // a full reconciliation on the next sync, which is safe by design.
            return new SyncStateFile();
        }
    }

    private async Task WriteToDiskAsync(SyncStateFile file, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = _filePath + ".tmp";
        using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, file, SerializerOptions, cancellationToken).ConfigureAwait(false);
        }

        File.Move(tempPath, _filePath, overwrite: true);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _lock.Dispose();
    }
}
