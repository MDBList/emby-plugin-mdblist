using Emby.Plugin.MDBList.Library;
using Xunit;

namespace Emby.Plugin.MDBList.Tests;

public class EpisodeRangeTests
{
    [Theory]
    [InlineData(5, null, 5)]
    [InlineData(5, 5, 5)]
    [InlineData(1, 2, 2)]
    [InlineData(3, 6, 6)]
    [InlineData(1, 1 + LibrarySnapshot.MaxEpisodeRangeSpan, 1 + LibrarySnapshot.MaxEpisodeRangeSpan)]
    public void ResolveEpisodeRangeEnd_KeepsPlausibleRanges(int start, int? end, int expected)
    {
        var (endNumber, rejected) = LibrarySnapshot.ResolveEpisodeRangeEnd(start, end);

        Assert.Equal(expected, endNumber);
        Assert.False(rejected);
    }

    [Theory]
    [InlineData(6, 4079526)]
    [InlineData(7, 1428313)]
    [InlineData(1, 75694)]
    [InlineData(1, 2 + LibrarySnapshot.MaxEpisodeRangeSpan)]
    [InlineData(8, 3)]
    [InlineData(-5, int.MaxValue)]
    public void ResolveEpisodeRangeEnd_CollapsesImplausibleRangesToSingleEpisode(int start, int end)
    {
        var (endNumber, rejected) = LibrarySnapshot.ResolveEpisodeRangeEnd(start, end);

        Assert.Equal(start, endNumber);
        Assert.True(rejected);
    }
}
