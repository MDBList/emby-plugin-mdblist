using System.Threading.Tasks;
using Emby.Plugin.MDBList.Api;
using Emby.Plugin.MDBList.Sync;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Plugins.UI.Views;

namespace Emby.Plugin.MDBList.UI;

/// <summary>
/// The plugin's config page controller -- Emby's modern
/// <c>IPluginUIPageController</c>/GenericEdit replacement for the classic
/// raw-HTML <c>IPluginConfigurationPage</c>. Auto-discovered via
/// <see cref="Plugin.UIPageControllers"/> (<c>IHasUIPages</c>) -- unlike
/// <c>IServerEntryPoint</c>/<c>IScheduledTask</c>/<c>IPluginConfigurationPage</c>,
/// this one is NOT independently auto-discovered by Emby's plugin loader;
/// confirmed by decompiling <c>UIPagesManager.RegisterPluginPageControllers</c>,
/// which only ever walks <c>IHasUIPages.UIPageControllers</c>.
/// </summary>
public class MDBListPageController : ControllerBase
{
    private readonly IUserManager _userManager;
    private readonly OAuthService _oauthService;
    private readonly SyncOrchestrator _orchestrator;
    private readonly SyncStateStore _stateStore;
    private readonly ILogManager _logManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="MDBListPageController"/> class.
    /// </summary>
    /// <param name="pluginInfo">The owning plugin's info.</param>
    /// <param name="userManager">Instance of the <see cref="IUserManager"/> interface.</param>
    /// <param name="oauthService">Instance of the <see cref="OAuthService"/>.</param>
    /// <param name="orchestrator">Instance of the <see cref="SyncOrchestrator"/>.</param>
    /// <param name="stateStore">Instance of the <see cref="SyncStateStore"/>.</param>
    /// <param name="logManager">Instance of the <see cref="ILogManager"/> interface.</param>
    public MDBListPageController(
        PluginInfo pluginInfo,
        IUserManager userManager,
        OAuthService oauthService,
        SyncOrchestrator orchestrator,
        SyncStateStore stateStore,
        ILogManager logManager)
        : base(pluginInfo.Id)
    {
        _userManager = userManager;
        _oauthService = oauthService;
        _orchestrator = orchestrator;
        _stateStore = stateStore;
        _logManager = logManager;

        PageInfo = new PluginPageInfo
        {
            Name = "MDBListMainPage",
            DisplayName = "MDBList",
            MenuIcon = "sync",
            EnableInMainMenu = true,
            IsMainConfigPage = true,
        };
    }

    /// <inheritdoc />
    public override PluginPageInfo PageInfo { get; }

    /// <inheritdoc />
    public override async Task<IPluginUIView> CreateDefaultPageView()
    {
        var view = new MDBListPageView(PluginId, _userManager, _oauthService, _orchestrator, _stateStore, _logManager);

        // The constructor only picks which user is initially selected --
        // loading that user's actual connected/toggle state awaits
        // SyncStateStore, so it happens here instead. Skipping this call
        // leaves every fresh page open showing the ContentData's
        // just-constructed defaults ("Not connected.") until the first
        // UserChanged postback triggers a real load.
        await view.InitializeAsync().ConfigureAwait(false);

        return view;
    }
}
