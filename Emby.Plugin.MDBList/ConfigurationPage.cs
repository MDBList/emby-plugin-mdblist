using System.IO;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Plugins;
using MDBListPlugin = Emby.Plugin.MDBList.Plugin;

namespace Emby.Plugin.MDBList;

/// <summary>
/// The plugin's config page -- Emby's analogue of Jellyfin's
/// <c>IHasWebPages.GetPages()</c>. Auto-discovered by implementing
/// <see cref="IPluginConfigurationPage"/>; streams the embedded HTML
/// directly rather than pointing at an embedded-resource path.
/// </summary>
public class ConfigurationPage : IPluginConfigurationPage
{
    /// <inheritdoc />
    public string Name => "MDBList";

    /// <inheritdoc />
    public ConfigurationPageType ConfigurationPageType => ConfigurationPageType.PluginConfiguration;

    /// <inheritdoc />
    public IPlugin Plugin => MDBListPlugin.Instance!;

    /// <inheritdoc />
    public Stream GetHtmlStream()
    {
        return GetType().Assembly.GetManifestResourceStream("Emby.Plugin.MDBList.Configuration.configPage.html")!;
    }
}
