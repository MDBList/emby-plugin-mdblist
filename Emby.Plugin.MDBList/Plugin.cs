using System;
using System.Collections.Generic;
using System.IO;
using Emby.Plugin.MDBList.Api;
using Emby.Plugin.MDBList.Configuration;
using Emby.Plugin.MDBList.Sync;
using Emby.Plugin.MDBList.UI;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Plugins.UI;
using MediaBrowser.Model.Serialization;

namespace Emby.Plugin.MDBList;

/// <summary>
/// The main plugin. Implements <see cref="IHasUIPages"/> to expose the
/// config page as a modern GenericEdit page controller -- confirmed by
/// decompiling <c>UIPagesManager.RegisterPluginPageControllers</c> that this
/// is the ONLY way such a controller gets registered (unlike
/// <c>IServerEntryPoint</c>/<c>IScheduledTask</c>/etc., which Emby's plugin
/// loader auto-discovers directly). Also implements <see cref="IHasThumbImage"/>
/// for the plugin's dashboard/catalog icon.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasUIPages, IHasThumbImage
{
    private readonly IUserManager _userManager;
    private readonly OAuthService _oauthService;
    private readonly SyncOrchestrator _orchestrator;
    private readonly SyncStateStore _stateStore;
    private readonly ILogManager _logManager;
    private List<IPluginUIPageController>? _pages;

    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
    /// <param name="userManager">Instance of the <see cref="IUserManager"/> interface.</param>
    /// <param name="oauthService">Instance of the <see cref="OAuthService"/>.</param>
    /// <param name="orchestrator">Instance of the <see cref="SyncOrchestrator"/>.</param>
    /// <param name="stateStore">Instance of the <see cref="SyncStateStore"/>.</param>
    /// <param name="logManager">Instance of the <see cref="ILogManager"/> interface.</param>
    public Plugin(
        IApplicationPaths applicationPaths,
        IXmlSerializer xmlSerializer,
        IUserManager userManager,
        OAuthService oauthService,
        SyncOrchestrator orchestrator,
        SyncStateStore stateStore,
        ILogManager logManager)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
        _userManager = userManager;
        _oauthService = oauthService;
        _orchestrator = orchestrator;
        _stateStore = stateStore;
        _logManager = logManager;
    }

    /// <inheritdoc />
    public override string Name => "MDBList";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("2fede946-59a5-4069-b47c-2cf1d5ac98be");

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public IReadOnlyCollection<IPluginUIPageController> UIPageControllers
    {
        get
        {
            _pages ??= new List<IPluginUIPageController>
            {
                new MDBListPageController(GetPluginInfo(), _userManager, _oauthService, _orchestrator, _stateStore, _logManager),
            };
            return _pages.AsReadOnly();
        }
    }

    /// <inheritdoc />
    public ImageFormat ThumbImageFormat => ImageFormat.Png;

    /// <inheritdoc />
    public Stream GetThumbImage()
    {
        var type = GetType();
        return type.Assembly.GetManifestResourceStream(type.Namespace + ".ThumbImage.png")!;
    }
}
