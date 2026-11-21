using System;
using System.Collections.Generic;
using Emby.Plugin.MDBList.Api.Models;
using Emby.Plugin.MDBList.Library;
using Emby.Plugin.MDBList.Sync;
using Xunit;

namespace Emby.Plugin.MDBList.Tests;

public class PhantomEpisodeTests
{
    private static readonly MediaIds Show = new() { Tmdb = 95479, Tvdb = 377543 };

    private static LibrarySnapshot SnapshotWithSeason(int season, int maxEpisode)
    {
        var snapshot = new LibrarySnapshot();
        for (var number = 1; number <= maxEpisode; number++)
        {
            snapshot.RegisterSeasonEpisode(Show, season, number);
        }

        return snapshot;
    }

    [Fact]
    public void IsImplausibleEpisode_FarBeyondSeasonMax_IsPhantom()
    {
        var snapshot = SnapshotWithSeason(1, 24);

        Assert.True(snapshot.IsImplausibleEpisode(Show, 1, 4079526));
        Assert.True(snapshot.IsImplausibleEpisode(Show, 1, 25 + LibrarySnapshot.MaxEpisodeRangeSpan));
    }

    [Fact]
    public void IsImplausibleEpisode_WithinSpanOfSeasonMax_IsKept()
    {
        var snapshot = SnapshotWithSeason(1, 24);

        Assert.False(snapshot.IsImplausibleEpisode(Show, 1, 5));
        Assert.False(snapshot.IsImplausibleEpisode(Show, 1, 24 + LibrarySnapshot.MaxEpisodeRangeSpan));
    }

    [Fact]
    public void IsImplausibleEpisode_SeasonOrShowNotInLibrary_IsKept()
    {
        var snapshot = SnapshotWithSeason(1, 24);

        Assert.False(snapshot.IsImplausibleEpisode(Show, 2, 4079526));
        Assert.False(snapshot.IsImplausibleEpisode(new MediaIds { Tmdb = 1 }, 1, 4079526));
        Assert.False(snapshot.IsImplausibleEpisode(Show, null, 4079526));
    }

    [Fact]
    public void IsImplausibleEpisode_MatchesByAnyProviderTheShowCarries()
    {
        var snapshot = SnapshotWithSeason(1, 24);

        Assert.True(snapshot.IsImplausibleEpisode(new MediaIds { Tvdb = 377543 }, 1, 4079526));
    }

    [Fact]
    public void FindPhantomKeys_DropsOnlyPhantomsMissingFromCurrentState()
    {
        var snapshot = SnapshotWithSeason(1, 24);
        var known = new Dictionary<string, KnownSyncItem>(StringComparer.Ordinal);
        for (var number = 1; number <= 5000; number++)
        {
            known[$"episode:tmdb:95479:1:{number}"] = new KnownSyncItem { Type = "episode", Ids = Show, Season = 1, Episode = number };
        }

        known["movie:tmdb:603"] = new KnownSyncItem { Type = "movie", Ids = new MediaIds { Tmdb = 603 } };
        var current = new Dictionary<string, KnownSyncItem>(StringComparer.Ordinal)
        {
            ["episode:tmdb:95479:1:6"] = known["episode:tmdb:95479:1:6"],
        };

        var phantoms = SyncPayloadBuilder.FindPhantomKeys(
            known,
            current,
            item => item.Type == "episode" && snapshot.IsImplausibleEpisode(item.Ids, item.Season, item.Episode));

        // Episodes 1-34 (up to 24 + span) stay; 35-5000 are phantoms; the movie stays.
        Assert.Equal(5000 - 24 - LibrarySnapshot.MaxEpisodeRangeSpan, phantoms.Count);
        Assert.DoesNotContain("episode:tmdb:95479:1:34", phantoms);
        Assert.Contains("episode:tmdb:95479:1:35", phantoms);
        Assert.DoesNotContain("movie:tmdb:603", phantoms);
    }
}
