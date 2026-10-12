using System.Collections.Generic;
using System.Globalization;

namespace Emby.Plugin.MDBList.Sync;

/// <summary>
/// Renders a stored <see cref="RunSummary"/> into a requested language. Kept
/// separate from the views so the same data renders correctly from the config
/// page (using the culture captured on its request thread), the status API
/// endpoint, and the server log in English.
/// </summary>
internal static class RunSummaryText
{
    /// <summary>
    /// Renders a stored summary, or returns null when no run has completed yet.
    /// </summary>
    /// <param name="summary">The stored summary, or null.</param>
    /// <param name="culture">The UI culture to render in, or null for the ambient one.</param>
    /// <returns>The rendered text, or null.</returns>
    internal static string? Format(RunSummary? summary, CultureInfo? culture)
    {
        if (summary is null)
        {
            return null;
        }

        if (summary.FailedMessage is not null)
        {
            return F(culture, nameof(Resources.Summary_Failed), summary.FailedMessage);
        }

        if (summary.SkippedEmptyLibrary)
        {
            return Get(nameof(Resources.Summary_EmptyLibrary), culture);
        }

        var parts = new List<string>(3);
        if (summary.Watched is not null)
        {
            parts.Add(FormatWatched(summary.Watched, summary.IsActivityCheck, culture));
        }

        if (summary.Ratings is not null)
        {
            parts.Add(FormatRatings(summary.Ratings, summary.IsActivityCheck, culture));
        }

        if (summary.Collection is not null)
        {
            parts.Add(FormatCollection(summary.Collection, culture));
        }

        var text = string.Join(Get(nameof(Resources.Summary_Separator), culture), parts);
        return summary.IsActivityCheck
            ? F(culture, nameof(Resources.Summary_ActivityPrefix), text)
            : text;
    }

    private static string FormatWatched(CategoryRunSummary category, bool isActivity, CultureInfo? culture)
    {
        if (category.Skipped)
        {
            return Get(nameof(Resources.Summary_WatchedSkipped), culture);
        }

        if (isActivity)
        {
            return F(
                culture,
                nameof(Resources.Summary_PullOnly_Watched),
                category.PulledApplied,
                SkippedSuffix(category.PullSkippedRemove, nameof(Resources.Summary_PullSkipped), culture),
                Mode(category.Mode, culture));
        }

        return F(
            culture,
            nameof(Resources.Summary_Watched),
            category.PushedAdd,
            category.PushedRemove,
            SkippedSuffix(category.PushSkippedRemove, nameof(Resources.Summary_PushSkipped), culture),
            category.PulledApplied,
            SkippedSuffix(category.PullSkippedRemove, nameof(Resources.Summary_PullSkipped), culture),
            Mode(category.Mode, culture));
    }

    private static string FormatRatings(CategoryRunSummary category, bool isActivity, CultureInfo? culture)
    {
        if (category.Skipped)
        {
            return Get(nameof(Resources.Summary_RatingsSkipped), culture);
        }

        if (isActivity)
        {
            return F(
                culture,
                nameof(Resources.Summary_PullOnly_Ratings),
                category.PulledApplied,
                Mode(category.Mode, culture));
        }

        return F(
            culture,
            nameof(Resources.Summary_Ratings),
            category.PushedAdd,
            category.PushedRemove,
            SkippedSuffix(category.PushSkippedRemove, nameof(Resources.Summary_PushSkipped), culture),
            category.PulledApplied,
            Mode(category.Mode, culture));
    }

    private static string FormatCollection(CategoryRunSummary category, CultureInfo? culture)
    {
        if (category.Skipped)
        {
            return Get(nameof(Resources.Summary_CollectionSkipped), culture);
        }

        return F(
            culture,
            nameof(Resources.Summary_Collection),
            category.PushedAdd,
            category.PushedRemove,
            SkippedSuffix(category.PushSkippedRemove, nameof(Resources.Summary_PushSkipped), culture));
    }

    private static string SkippedSuffix(int? count, string key, CultureInfo? culture)
    {
        return count is > 0 ? F(culture, key, count) : string.Empty;
    }

    private static string Mode(string? mode, CultureInfo? culture)
    {
        return mode switch
        {
            "full" => Get(nameof(Resources.Summary_Mode_Full), culture),
            "incremental" => Get(nameof(Resources.Summary_Mode_Incremental), culture),
            _ => mode ?? string.Empty,
        };
    }

    private static string Get(string key, CultureInfo? culture)
    {
        return Resources.ResourceManager.GetString(key, culture) ?? key;
    }

    private static string F(CultureInfo? culture, string key, params object?[] args)
    {
        return string.Format(culture, Get(key, culture), args);
    }
}