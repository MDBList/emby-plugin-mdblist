using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Emby.Plugin.MDBList.Api.Models;
using Emby.Plugin.MDBList.Library;
using Emby.Plugin.MDBList.Sync;
using Xunit;
using Xunit.Abstractions;

namespace Emby.Plugin.MDBList.Tests;

/// <summary>
/// Over a real sync_state.json shaped like the one GitHub issue #8's cancelled
/// run left behind: the real watched history plus a long run of phantom
/// episodes from a misparsed "S01E06-E4079526" file.
/// </summary>
public sealed class PhantomStateCleanupTests : IDisposable
{
    private const int RealEpisodes = 24;
    private const int PhantomEnd = 50_000;

    private static readonly MediaIds Show = new() { Tmdb = 95479, Tvdb = 377543 };
    private static readonly MediaIds OtherShow = new() { Tmdb = 1399 };

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "mdblist-phantom-" + Guid.NewGuid().ToString("N"));
    private readonly ITestOutputHelper _output;
    private readonly Guid _userId = Guid.NewGuid();

    public PhantomStateCleanupTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private string FilePath => Path.Combine(_directory, "sync_state.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task BrokenState_WithoutCleanup_TripsTheRemovalCircuitBreaker()
    {
        var store = await SeedBrokenStateAsync();
        var known = await store.GetKnownItemsAsync(_userId, SyncCategory.Watched, CancellationToken.None);
        var current = CurrentWatched();

        var toRemove = known.Keys.Count(key => !current.ContainsKey(key));
        var threshold = Math.Max(SyncPayloadBuilder.RemovalMinBatch, (int)(known.Count * SyncPayloadBuilder.RemovalMaxFraction));
        _output.WriteLine($"without cleanup: known={known.Count}, toRemove={toRemove}, threshold={threshold}");

        // Same comparison DiffAndReconcileAsync makes: the removal is refused,
        // nothing is persisted, and every later run re-diffs to the same result.
        Assert.True(toRemove > threshold);
    }

    [Fact]
    public async Task BrokenState_Cleanup_DropsPhantomsFromTheFileAndKeepsRealHistory()
    {
        var store = await SeedBrokenStateAsync();
        var sizeBefore = new FileInfo(FilePath).Length;
        var snapshot = Snapshot();
        var current = CurrentWatched();

        var known = await store.GetKnownItemsAsync(_userId, SyncCategory.Watched, CancellationToken.None);
        var dropped = await SyncPayloadBuilder.PrunePhantomsAsync(store, _userId, SyncCategory.Watched, known, current, IsPhantom(snapshot), CancellationToken.None);

        // Re-read from disk with a fresh store, as the next run would.
        var reloaded = await SyncStateStore.ForFile(FilePath).GetKnownItemsAsync(_userId, SyncCategory.Watched, CancellationToken.None);
        var sizeAfter = new FileInfo(FilePath).Length;
        _output.WriteLine($"with cleanup: dropped={dropped}, file {sizeBefore:N0} -> {sizeAfter:N0} bytes, known left={reloaded.Count}");
        _output.WriteLine(PrettyEntry(FilePath));

        Assert.Equal(PhantomEnd - RealEpisodes - LibrarySnapshot.MaxEpisodeRangeSpan, dropped);
        Assert.Equal(known.Keys.OrderBy(k => k), reloaded.Keys.OrderBy(k => k));

        // Real history survives: E1-E24, E25-E34 (within the span, kept as an
        // ordinary small removal), 10 of the other show, 3 movies with dates.
        Assert.Equal(RealEpisodes + LibrarySnapshot.MaxEpisodeRangeSpan + 10 + 3, reloaded.Count);
        Assert.All(Enumerable.Range(1, RealEpisodes), n => Assert.Contains($"episode:tmdb:95479:1:{n}", reloaded.Keys));
        Assert.Contains("episode:tmdb:1399:1:10", reloaded.Keys);
        Assert.Equal("2023-02-09T03:57:08", reloaded["movie:tmdb:603"].WatchedAt);
        Assert.DoesNotContain("episode:tmdb:95479:1:4000", reloaded.Keys);
        Assert.True(sizeAfter < sizeBefore / 100);

        // After cleanup, what's left to remove is small enough to pass the breaker.
        var toRemove = reloaded.Keys.Count(key => !current.ContainsKey(key));
        var threshold = Math.Max(SyncPayloadBuilder.RemovalMinBatch, (int)(reloaded.Count * SyncPayloadBuilder.RemovalMaxFraction));
        Assert.True(toRemove <= threshold, $"toRemove={toRemove}, threshold={threshold}");

        // A second run finds nothing more to drop.
        Assert.Equal(0, await SyncPayloadBuilder.PrunePhantomsAsync(store, _userId, SyncCategory.Watched, reloaded, current, IsPhantom(snapshot), CancellationToken.None));
    }

    private static Func<KnownSyncItem, bool> IsPhantom(LibrarySnapshot snapshot)
    {
        return item => item.Type == "episode" && snapshot.IsImplausibleEpisode(item.Ids, item.Season, item.Episode);
    }

    // The state an older version leaves: real watched items, then the
    // phantom S01E25..E50000 run under the same show.
    private async Task<SyncStateStore> SeedBrokenStateAsync()
    {
        var store = SyncStateStore.ForFile(FilePath);
        var items = new Dictionary<string, KnownSyncItem>(StringComparer.Ordinal);
        for (var n = 1; n <= PhantomEnd; n++)
        {
            items[$"episode:tmdb:95479:1:{n}"] = Episode(Show, 1, n);
        }

        foreach (var (key, item) in CurrentWatched())
        {
            items[key] = item;
        }

        await store.MergeKnownItemsAsync(_userId, SyncCategory.Watched, items, [], CancellationToken.None);
        return store;
    }

    private static LibrarySnapshot Snapshot()
    {
        var snapshot = new LibrarySnapshot();
        for (var n = 1; n <= RealEpisodes; n++)
        {
            snapshot.RegisterSeasonEpisode(Show, 1, n);
        }

        for (var n = 1; n <= 10; n++)
        {
            snapshot.RegisterSeasonEpisode(OtherShow, 1, n);
        }

        return snapshot;
    }

    // What the fixed snapshot reports as watched now.
    private static Dictionary<string, KnownSyncItem> CurrentWatched()
    {
        var current = new Dictionary<string, KnownSyncItem>(StringComparer.Ordinal);
        for (var n = 1; n <= RealEpisodes; n++)
        {
            current[$"episode:tmdb:95479:1:{n}"] = Episode(Show, 1, n);
        }

        for (var n = 1; n <= 10; n++)
        {
            current[$"episode:tmdb:1399:1:{n}"] = Episode(OtherShow, 1, n);
        }

        foreach (var tmdb in new[] { 603, 604, 605 })
        {
            current[$"movie:tmdb:{tmdb}"] = new KnownSyncItem { Type = "movie", Ids = new MediaIds { Tmdb = tmdb }, WatchedAt = "2023-02-09T03:57:08" };
        }

        return current;
    }

    private static KnownSyncItem Episode(MediaIds show, int season, int number)
    {
        return new KnownSyncItem { Type = "episode", Ids = show, Season = season, Episode = number };
    }

    // First ~600 chars of the file, to show its on-disk shape in test output.
    private static string PrettyEntry(string path)
    {
        var json = JsonSerializer.Serialize(JsonDocument.Parse(File.ReadAllText(path)).RootElement, new JsonSerializerOptions { WriteIndented = true });
        return json.Length > 600 ? json[..600] + "\n..." : json;
    }
}
