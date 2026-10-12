using System.Collections.Generic;
using System.ComponentModel;
using Emby.Web.GenericEdit;
using Emby.Web.GenericEdit.Common;
using Emby.Web.GenericEdit.Elements;
using MediaBrowser.Model.Attributes;
using MediaBrowser.Model.LocalizationAttributes;

namespace Emby.Plugin.MDBList.UI;

/// <summary>
/// The GenericEdit view-model backing the config page. This is a pure
/// display/command surface -- <see cref="Configuration.PluginConfiguration.Users"/>
/// stays the actual persisted store, keyed by Emby user id; this object is
/// rebuilt from that store on every page load/user switch and written back
/// to it only on <see cref="MDBListPageView.OnSaveCommand"/>. Field shapes
/// (types/attributes) confirmed by reflection over the real
/// <c>Emby.Web.GenericEdit.dll</c>/<c>MediaBrowser.Model.dll</c> and
/// cross-checked against the real, currently-maintained
/// <c>emby-playlist-manager</c> plugin's own <c>PluginOptions.cs</c>.
/// </summary>
public class MDBListOptions : EditableOptionsBase
{
    /// <inheritdoc />
    public override string EditorTitle => "MDBList";

    /// <inheritdoc />
    public override string EditorDescription => Resources.Settings_Description;

    /// <summary>
    /// Gets or sets the source list for <see cref="SelectedUserId"/> --
    /// hidden from the form itself (<see cref="Browsable"/> false), only
    /// feeds the dropdown.
    /// </summary>
    [Browsable(false)]
    public IEnumerable<EditorSelectOption> AvailableUsers { get; set; } = new List<EditorSelectOption>();

    /// <summary>
    /// Gets or sets the currently-selected Emby user's id (as a string --
    /// the framework's select-source binding is string-keyed).
    /// </summary>
    [DisplayNameL("User_Name", ResourceType = typeof(Resources))]
    [DescriptionL("User_Desc", ResourceType = typeof(Resources))]
    [SelectItemsSource(nameof(AvailableUsers))]
    [AutoPostBack("UserChanged", nameof(SelectedUserId))]
    public string SelectedUserId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the connection status display.
    /// </summary>
    public StatusItem ConnectionStatus { get; set; } = new(Resources.Status_Connection, Resources.Status_NotConnected, ItemStatus.Unavailable);

    /// <summary>
    /// Gets or sets the device-authorization user code, shown while a
    /// Connect flow is waiting for the user to approve on mdblist.com.
    /// </summary>
    public LabelItem DeviceCode { get; set; } = new(string.Empty) { IsVisible = false };

    /// <summary>
    /// Gets or sets the verification link shown alongside <see cref="DeviceCode"/>.
    /// </summary>
    public LabelItem VerifyLink { get; set; } = new(string.Empty) { IsVisible = false, Icon = IconNames.open_in_new };

    /// <summary>
    /// Gets or sets the "Connect to MDBList" button.
    /// </summary>
    public ButtonItem ConnectButton { get; set; } = new(Resources.Button_Connect) { Icon = IconNames.link, Data1 = "Connect" };

    /// <summary>
    /// Gets or sets the "Disconnect" button.
    /// </summary>
    public ButtonItem DisconnectButton { get; set; } = new(Resources.Button_Disconnect)
    {
        Icon = IconNames.link_off,
        Data1 = "Disconnect",
        IsVisible = false,
        ConfirmationPrompt = Resources.Button_DisconnectPrompt,
    };

    /// <summary>
    /// Gets or sets the "Test connection" button.
    /// </summary>
    public ButtonItem TestButton { get; set; } = new(Resources.Button_Test) { Icon = IconNames.cloud_done, Data1 = "Test", IsVisible = false };

    /// <summary>
    /// Gets or sets a spacer between the connection block and the sync settings.
    /// </summary>
    public SpacerItem Spacer1 { get; set; } = new();

    /// <summary>
    /// Gets or sets the sync-settings section caption -- hidden until connected.
    /// </summary>
    public CaptionItem SettingsCaption { get; set; } = new(Resources.Caption_WhatToSync) { IsVisible = false };

    /// <summary>
    /// Gets or sets a value indicating whether watched status syncs.
    /// </summary>
    [DisplayNameL("Opt_Watched_Name", ResourceType = typeof(Resources))]
    [DescriptionL("Opt_Watched_Desc", ResourceType = typeof(Resources))]
    public bool WatchedEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether ratings sync.
    /// </summary>
    [DisplayNameL("Opt_Ratings_Name", ResourceType = typeof(Resources))]
    [DescriptionL("Opt_Ratings_Desc", ResourceType = typeof(Resources))]
    public bool RatingsEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether collection membership pushes.
    /// </summary>
    [DisplayNameL("Opt_Collection_Name", ResourceType = typeof(Resources))]
    [DescriptionL("Opt_Collection_Desc", ResourceType = typeof(Resources))]
    public bool CollectionEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether live playback progress scrobbles.
    /// </summary>
    [DisplayNameL("Opt_Scrobbling_Name", ResourceType = typeof(Resources))]
    [DescriptionL("Opt_Scrobbling_Desc", ResourceType = typeof(Resources))]
    public bool ScrobblingEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether Emby's thumbs-up/down should
    /// be ignored rather than pushed as a rating.
    /// </summary>
    [DisplayNameL("Opt_IgnoreThumb_Name", ResourceType = typeof(Resources))]
    [DescriptionL("Opt_IgnoreThumb_Desc", ResourceType = typeof(Resources))]
    public bool IgnoreThumbRatings { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether a full sync follows a library scan.
    /// </summary>
    [DisplayNameL("Opt_SyncAfterScan_Name", ResourceType = typeof(Resources))]
    [DescriptionL("Opt_SyncAfterScan_Desc", ResourceType = typeof(Resources))]
    public bool SyncAfterLibraryScan { get; set; } = true;

    /// <summary>
    /// Gets or sets a spacer between the sync settings and the status section.
    /// </summary>
    public SpacerItem Spacer2 { get; set; } = new();

    /// <summary>
    /// Gets or sets the status section caption -- hidden until connected.
    /// </summary>
    public CaptionItem StatusCaption { get; set; } = new(Resources.Caption_Status) { IsVisible = false };

    /// <summary>
    /// Gets or sets the last-run summary display.
    /// </summary>
    public StatusItem LastRunStatus { get; set; } = new(Resources.Status_LastRun, Resources.Status_NoSyncRun, ItemStatus.Unavailable);

    /// <summary>
    /// Gets or sets the "Sync now" button.
    /// </summary>
    public ButtonItem SyncNowButton { get; set; } = new(Resources.Button_SyncNow) { Icon = IconNames.sync, Data1 = "SyncNow", IsVisible = false };
}
