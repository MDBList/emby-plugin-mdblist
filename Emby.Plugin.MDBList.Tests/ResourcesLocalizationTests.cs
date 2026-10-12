using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Resources;
using Xunit;

namespace Emby.Plugin.MDBList.Tests;

/// <summary>
/// Guards the plugin UI resources: the neutral English set and the Simplified
/// Chinese satellite must carry the same keys, with no blanks, and every
/// strongly-typed accessor on <c>Resources</c> must resolve to a real entry.
/// </summary>
public class ResourcesLocalizationTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");
    private static readonly CultureInfo SimplifiedChinese = CultureInfo.GetCultureInfo("zh-CN");

    [Fact]
    public void EnglishAndChinese_HaveIdenticalKeySets()
    {
        var englishKeys = Keys(English);
        var chineseKeys = Keys(SimplifiedChinese);

        Assert.NotEmpty(englishKeys);
        Assert.Equal(
            englishKeys.OrderBy(k => k, StringComparer.Ordinal),
            chineseKeys.OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public void EveryAccessor_ResolvesInBothLanguages()
    {
        var accessors = typeof(Emby.Plugin.MDBList.Resources)
            .GetProperties(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            .Where(p => p.PropertyType == typeof(string))
            .Select(p => p.Name)
            .ToList();

        Assert.NotEmpty(accessors);

        foreach (var key in accessors)
        {
            Assert.False(string.IsNullOrWhiteSpace(ResourceManager.GetString(key, English)), key);
            Assert.False(string.IsNullOrWhiteSpace(ResourceManager.GetString(key, SimplifiedChinese)), key);
        }
    }

    [Fact]
    public void ChineseValues_DifferFromEnglish()
    {
        // The strongest guard that the Chinese satellite really loaded: if it
        // silently fell back to the neutral English resources, every value
        // would be identical and this count would be zero.
        var differing = Keys(English).Count(key =>
            ResourceManager.GetString(key, English) != ResourceManager.GetString(key, SimplifiedChinese));

        Assert.True(differing >= 40, $"Only {differing} keys differ between en and zh-CN -- is the Simplified Chinese satellite loading?");
    }

    private static ResourceManager ResourceManager => Emby.Plugin.MDBList.Resources.ResourceManager;

    private static string[] Keys(CultureInfo culture)
    {
        var set = ResourceManager.GetResourceSet(culture, createIfNotExists: true, tryParents: true);
        Assert.NotNull(set);
        return set!.Cast<DictionaryEntry>().Select(e => (string)e.Key).ToArray();
    }
}