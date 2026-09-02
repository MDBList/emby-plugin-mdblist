using System;
using Emby.Plugin.MDBList.Api.Models;

namespace Emby.Plugin.MDBList.Library;

/// <summary>
/// One movie or episode row in a <see cref="LibrarySnapshot"/> -- port of
/// jellyfin-plugin-mdblist's SnapshotItem.cs (itself the record dict shape
/// in library_snapshot.py). <see cref="LastPlayedDate"/> and
/// <see cref="DateCreated"/> are <see cref="DateTimeOffset"/>, not
/// <see cref="DateTime"/> like the Jellyfin sibling -- that's the real
/// property type on Emby's <c>UserItemData</c>/<c>BaseItem</c>, confirmed
/// by reflection.
/// </summary>
public class SnapshotItem
{
    /// <summary>
    /// Gets the item type: "movie" or "episode".
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Gets the Emby item id.
    /// </summary>
    public required Guid ItemId { get; init; }

    /// <summary>
    /// Gets the item's display title, for logging.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// Gets the provider ids to match against: the movie's own ids, or the
    /// parent show's ids for an episode (matches the actual MDBList API
    /// contract, which keys episode rows by show ids + season/episode, not
    /// by any id the episode itself might carry).
    /// </summary>
    public required MediaIds Ids { get; init; }

    /// <summary>
    /// Gets the season number (episodes only).
    /// </summary>
    public int? Season { get; init; }

    /// <summary>
    /// Gets the episode number (episodes only).
    /// </summary>
    public int? EpisodeNumber { get; init; }

    /// <summary>
    /// Gets a value indicating whether this item is marked played.
    /// </summary>
    public bool Played { get; init; }

    /// <summary>
    /// Gets the play count.
    /// </summary>
    public int PlayCount { get; init; }

    /// <summary>
    /// Gets when this item was last played, UTC.
    /// </summary>
    public DateTimeOffset? LastPlayedDate { get; init; }

    /// <summary>
    /// Gets the user's rating (0-10), or null if unrated.
    /// </summary>
    public double? Rating { get; init; }

    /// <summary>
    /// Gets when this item was added to the library, UTC -- used as the
    /// "collected at" timestamp for collection sync.
    /// </summary>
    public DateTimeOffset DateCreated { get; init; }
}
