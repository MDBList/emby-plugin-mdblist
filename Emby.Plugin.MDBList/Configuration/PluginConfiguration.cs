using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using MediaBrowser.Model.Plugins;

namespace Emby.Plugin.MDBList.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the linked users. V1 only ever holds one entry -- see
    /// <see cref="UserSyncConfig"/>.
    ///
    /// Settable rather than the usual get-only-collection pattern: the
    /// config page saves settings by POSTing the whole configuration object
    /// back through the dashboard's own config-save endpoint, which
    /// deserializes the request body with System.Text.Json. The Jellyfin
    /// sibling plugin hit this exact bug (a get-only collection silently
    /// deserializes empty, erasing every linked user on save) and fixed it
    /// by making the property settable -- applying that fix here from the
    /// start rather than waiting to hit it again.
    /// </summary>
    [SuppressMessage("Design", "CA2227:CollectionPropertiesShouldBeReadOnly", Justification = "Must be settable for System.Text.Json to populate it via the dashboard's own config-save endpoint.")]
    public Collection<UserSyncConfig> Users { get; set; } = new();
}
