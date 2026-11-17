using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emby.Plugin.MDBList.Sync;
using Xunit;

namespace Emby.Plugin.MDBList.Tests;

public sealed class SyncStateStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "mdblist-state-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_directory, "sync_state.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    // Emby hands each scheduled task its own store instance (GitHub issue #7):
    // concurrent writes from two of them must neither fail on the temp file
    // nor overwrite each other's changes.
    [Fact]
    public async Task MergeKnownItems_FromTwoInstancesAtOnce_KeepsEveryWrite()
    {
        var userId = Guid.NewGuid();
        var first = SyncStateStore.ForFile(FilePath);
        var second = SyncStateStore.ForFile(FilePath);

        var writes = Enumerable.Range(0, 40).Select(i =>
        {
            var store = i % 2 == 0 ? first : second;
            var upserts = new Dictionary<string, KnownSyncItem>(StringComparer.Ordinal)
            {
                [$"movie:tmdb:{i}"] = new KnownSyncItem { Type = "movie" },
            };
            return Task.Run(() => store.MergeKnownItemsAsync(userId, SyncCategory.Watched, upserts, [], CancellationToken.None));
        });
        await Task.WhenAll(writes);

        var known = await SyncStateStore.ForFile(FilePath).GetKnownItemsAsync(userId, SyncCategory.Watched, CancellationToken.None);
        Assert.Equal(40, known.Count);
    }

    [Fact]
    public async Task Write_LeavesNoTempFileBehind()
    {
        var store = SyncStateStore.ForFile(FilePath);

        await store.SetSyncedAtAsync(Guid.NewGuid(), SyncCategory.Watched, "2026-11-17T00:00:00Z", CancellationToken.None);

        Assert.Equal(["sync_state.json"], Directory.GetFiles(_directory).Select(Path.GetFileName));
    }
}
