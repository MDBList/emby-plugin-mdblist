using System;
using System.Threading.Tasks;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Events;
using MediaBrowser.Model.GenericEdit;
using MediaBrowser.Model.Plugins.UI.Views;
using MediaBrowser.Model.Plugins.UI.Views.Enums;

namespace Emby.Plugin.MDBList.UI;

/// <summary>
/// Vendored base for <see cref="IPluginUIView"/> -- like <see cref="ControllerBase"/>,
/// ported from <c>emby-playlist-manager</c>'s own <c>UIBaseClasses/Views/PluginPageView.cs</c>
/// boilerplate, since Emby ships no concrete base for plugins to build on.
/// </summary>
public abstract class PluginViewBase : IPluginUIView, IPluginViewWithOptions
{
    private IEditableObject? _contentDataCore;

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginViewBase"/> class.
    /// </summary>
    /// <param name="pluginId">The owning plugin's id.</param>
    protected PluginViewBase(string pluginId)
    {
        PluginId = pluginId;
    }

    /// <inheritdoc />
    public event EventHandler<GenericEventArgs<IPluginUIView>>? UIViewInfoChanged;

    /// <inheritdoc />
    public virtual string? Caption => ContentData?.EditorTitle;

    /// <inheritdoc />
    public virtual string? SubCaption => ContentData?.EditorDescription;

    /// <inheritdoc />
    public string PluginId { get; }

    /// <inheritdoc />
    public IEditableObject? ContentData
    {
        get => _contentDataCore;
        set => _contentDataCore = value;
    }

    /// <inheritdoc />
    public UserDto? User { get; set; }

    /// <inheritdoc />
    public string? RedirectViewUrl { get; set; }

    /// <inheritdoc />
    public QueryCloseAction QueryCloseAction { get; set; }

    /// <inheritdoc />
    public WizardHidingBehavior WizardHidingBehavior { get; set; }

    /// <inheritdoc />
    public CompactViewAppearance CompactViewAppearance { get; set; }

    /// <inheritdoc />
    public DialogSize DialogSize { get; set; }

    /// <inheritdoc />
    public string? OKButtonCaption { get; set; }

    /// <inheritdoc />
    public DialogAction PrimaryDialogAction { get; set; }

    /// <inheritdoc />
    public virtual bool IsCommandAllowed(string commandKey) => true;

    /// <inheritdoc />
    public virtual Task<IPluginUIView> RunCommand(string itemId, string commandId, string data)
        => Task.FromResult<IPluginUIView>(null!);

    /// <inheritdoc />
    public virtual Task Cancel() => Task.CompletedTask;

    /// <inheritdoc />
    public virtual void OnDialogResult(IPluginUIView dialogView, bool completedOk, object data)
    {
    }

    /// <inheritdoc />
    public virtual PluginViewOptions ViewOptions => new()
    {
        QueryCloseAction = QueryCloseAction,
        WizardHidingBehavior = WizardHidingBehavior,
        CompactViewAppearance = CompactViewAppearance,
        DialogSize = DialogSize,
        OKButtonCaption = OKButtonCaption,
        PrimaryDialogAction = PrimaryDialogAction,
    };

    /// <summary>
    /// Raises <see cref="UIViewInfoChanged"/> -- pushes the current
    /// <see cref="ContentData"/> to any open session out-of-band, via
    /// Emby's own admin-session websocket channel, without a page reload.
    /// </summary>
    protected void RaiseUIViewInfoChanged()
        => UIViewInfoChanged?.Invoke(this, new GenericEventArgs<IPluginUIView>(this));
}

/// <summary>
/// Vendored base for <see cref="IPluginPageView"/>, adding the page-level
/// Save/Back affordances on top of <see cref="PluginViewBase"/>.
/// </summary>
public abstract class PluginPageView : PluginViewBase, IPluginPageView
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginPageView"/> class.
    /// </summary>
    /// <param name="pluginId">The owning plugin's id.</param>
    protected PluginPageView(string pluginId)
        : base(pluginId)
    {
    }

    /// <inheritdoc />
    public bool ShowSave { get; set; } = true;

    /// <inheritdoc />
    public bool ShowBack { get; set; }

    /// <inheritdoc />
    public bool AllowSave { get; set; } = true;

    /// <inheritdoc />
    public bool AllowBack { get; set; } = true;

    /// <inheritdoc />
    public virtual Task<IPluginUIView> OnSaveCommand(string itemId, string commandId, string data)
        => Task.FromResult<IPluginUIView>(this);
}
