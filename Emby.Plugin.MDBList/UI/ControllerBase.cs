using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Plugins.UI;
using MediaBrowser.Model.Plugins.UI.Views;

namespace Emby.Plugin.MDBList.UI;

/// <summary>
/// Vendored base for <see cref="IPluginUIPageController"/> -- Emby ships no
/// concrete implementation for plugins to build on, only the interface.
/// Ported from the real, currently-maintained <c>emby-playlist-manager</c>
/// plugin's own <c>UIBaseClasses/ControllerBase.cs</c> boilerplate, which
/// exists for exactly this reason.
/// </summary>
public abstract class ControllerBase : IPluginUIPageController
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ControllerBase"/> class.
    /// </summary>
    /// <param name="pluginId">The owning plugin's id.</param>
    protected ControllerBase(string pluginId)
    {
        PluginId = pluginId;
    }

    /// <inheritdoc />
    public abstract PluginPageInfo PageInfo { get; }

    /// <summary>
    /// Gets the owning plugin's id.
    /// </summary>
    public string PluginId { get; }

    /// <inheritdoc />
    public virtual Task Initialize(CancellationToken token) => Task.CompletedTask;

    /// <inheritdoc />
    public abstract Task<IPluginUIView> CreateDefaultPageView();
}
