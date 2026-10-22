using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Logging;

namespace Emby.Plugin.MDBList.Library;

/// <summary>
/// Reads the media info of a library item's versions (media sources) for the
/// collection push -- port of jellyfin-plugin-mdblist's MediaInfoReader.cs.
/// Each item's result is cached until the item is saved again (DateLastSaved,
/// which also moves when a probe fills in its streams) or its file changes
/// (DateModified), and for at most about CacheLifetime: a version added next to
/// an item (a sibling with the same presentation key) doesn't move its dates.
/// Reading media streams costs several database queries per item and a sync
/// touches the whole library, so empty results (e.g. .strm items) are cached
/// too; an unreadable item (null) is not. The cache is static because Emby
/// builds a separate object graph for each consumer (scheduled task, service,
/// debouncer), which would otherwise each start cold.
/// </summary>
public class MediaInfoReader
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger _logger;
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(6);

    // Expiry on the monotonic tick count, so a wall-clock jump can't keep entries forever.
    private static readonly ConcurrentDictionary<Guid, (DateTimeOffset Saved, DateTimeOffset Modified, long ExpiresAtMs, IReadOnlyList<MediaCopy> Copies)> Cache = new();
    private static int _reads;

    /// <summary>
    /// Initializes a new instance of the <see cref="MediaInfoReader"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="logManager">Instance of the <see cref="ILogManager"/> interface.</param>
    public MediaInfoReader(ILibraryManager libraryManager, ILogManager logManager)
    {
        _libraryManager = libraryManager;
        _logger = logManager.GetLogger("MDBList.Sync");
    }

    /// <summary>
    /// The copies (one per version with known media info) of a library item.
    /// </summary>
    /// <param name="record">The library item's snapshot record; its dates check the cache.</param>
    /// <returns>The copies; empty when the item has none, null when they couldn't be read (unknown).</returns>
    public IReadOnlyList<MediaCopy>? ReadCopies(SnapshotItem record)
    {
        var itemId = record.ItemId;
        try
        {
            if (Interlocked.Increment(ref _reads) % 5000 == 0)
            {
                PruneExpired();
            }

            // A cache hit costs no item lookup: the snapshot loaded the item just
            // now, with its current dates.
            if (Cache.TryGetValue(itemId, out var cached) && cached.Saved == record.DateLastSaved
                && cached.Modified == record.DateModified && Environment.TickCount64 < cached.ExpiresAtMs)
            {
                return cached.Copies;
            }

            var item = _libraryManager.GetItemById(itemId);
            if (item is null)
            {
                return null; // gone or replaced since the snapshot: unknown, not "no copies"
            }

            var saved = item.DateLastSaved;
            var modified = item.DateModified;
            var ownSourceId = "mediasource_" + item.InternalId.ToString(CultureInfo.InvariantCulture);

            // Alternate versions included: Emby's library query returns one item
            // per title, so the other versions are only reachable through it.
            var sources = item.GetMediaSources(true, false, _libraryManager.GetLibraryOptions(item));
            var copies = new List<MediaCopy>();
            foreach (var source in sources)
            {
                var copy = MediaInfoMapper.FromSource(source);
                if (copy is not null)
                {
                    copy.IsOwn = string.Equals(copy.CopyKey, ownSourceId, StringComparison.OrdinalIgnoreCase);
                    copies.Add(copy);
                }
            }

            // Jittered so a library read at once doesn't all expire at once.
            var lifetime = CacheLifetime * (0.75 + (0.5 * Random.Shared.NextDouble()));
            Cache[itemId] = (saved, modified, Environment.TickCount64 + (long)lifetime.TotalMilliseconds, copies);

            return copies;
        }
        catch (Exception ex)
        {
            // Media info is optional: an unreadable item still syncs as collected,
            // and "unknown" never counts as a change (see CollectionSync.MediaChanged).
            _logger.ErrorException("MDBList Sync: could not read media info of item {0}", ex, itemId);
            return null;
        }
    }

    private static void PruneExpired()
    {
        var now = Environment.TickCount64;
        foreach (var (id, entry) in Cache)
        {
            if (now >= entry.ExpiresAtMs)
            {
                Cache.TryRemove(id, out _);
            }
        }
    }
}
