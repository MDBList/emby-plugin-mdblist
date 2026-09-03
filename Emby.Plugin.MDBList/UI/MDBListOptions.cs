using System.Collections.Generic;
using System.ComponentModel;
using Emby.Web.GenericEdit;
using Emby.Web.GenericEdit.Common;
using Emby.Web.GenericEdit.Elements;
using MediaBrowser.Model.Attributes;

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
    public override string EditorDescription =>
        "Two-way sync of watched status and ratings, collection membership, and live playback scrobbling with MDBList.";

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
    [DisplayName("Emby user")]
    [Description("Each Emby user can connect their own MDBList account and set their own sync options independently.")]
    [SelectItemsSource(nameof(AvailableUsers))]
    [AutoPostBack("UserChanged", nameof(SelectedUserId))]
    public string SelectedUserId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the connection status display.
    /// </summary>
    public StatusItem ConnectionStatus { get; set; } = new("Connection", "Not connected.", ItemStatus.Unavailable);

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
    public ButtonItem ConnectButton { get; set; } = new("Connect to MDBList") { Icon = IconNames.link, Data1 = "Connect" };

    /// <summary>
    /// Gets or sets the "Disconnect" button.
    /// </summary>
    public ButtonItem DisconnectButton { get; set; } = new("Disconnect")
    {
        Icon = IconNames.link_off,
        Data1 = "Disconnect",
        IsVisible = false,
        ConfirmationPrompt = "Disconnect this Emby user from MDBList?",
    };

    /// <summary>
    /// Gets or sets the "Test connection" button.
    /// </summary>
    public ButtonItem TestButton { get; set; } = new("Test connection") { Icon = IconNames.cloud_done, Data1 = "Test", IsVisible = false };

    /// <summary>
    /// Gets or sets a spacer between the connection block and the sync settings.
    /// </summary>
    public SpacerItem Spacer1 { get; set; } = new();

    /// <summary>
    /// Gets or sets the sync-settings section caption -- hidden until connected.
    /// </summary>
    public CaptionItem SettingsCaption { get; set; } = new("What to sync") { IsVisible = false };

    /// <summary>
    /// Gets or sets a value indicating whether watched status syncs.
    /// </summary>
    [DisplayName("Watched status")]
    [Description("Two-way sync of watched/unwatched state.")]
    public bool WatchedEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether ratings sync.
    /// </summary>
    [DisplayName("Ratings")]
    [Description("Two-way sync of ratings.")]
    public bool RatingsEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether collection membership pushes.
    /// </summary>
    [DisplayName("Collection membership")]
    [Description("Pushes which movies/shows are in your library to MDBList.")]
    public bool CollectionEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether live playback progress scrobbles.
    /// </summary>
    [DisplayName("Live scrobbling")]
    [Description("Reports playback progress to MDBList in real time as you watch, independent of watched-status sync above.")]
    public bool ScrobblingEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether Emby's thumbs-up/down should
    /// be ignored rather than pushed as a rating.
    /// </summary>
    [DisplayName("Ignore thumbs-up/down as ratings")]
    [Description("Emby's thumbs button always writes a 10 or 1 rating regardless of intent -- leave this checked unless you want that pushed to MDBList as a real rating.")]
    public bool IgnoreThumbRatings { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether a full sync follows a library scan.
    /// </summary>
    [DisplayName("Sync after library scan")]
    [Description("Runs a full sync a few seconds after the library changes.")]
    public bool SyncAfterLibraryScan { get; set; } = true;

    /// <summary>
    /// Gets or sets a spacer between the sync settings and the status section.
    /// </summary>
    public SpacerItem Spacer2 { get; set; } = new();

    /// <summary>
    /// Gets or sets the status section caption -- hidden until connected.
    /// </summary>
    public CaptionItem StatusCaption { get; set; } = new("Status") { IsVisible = false };

    /// <summary>
    /// Gets or sets the last-run summary display.
    /// </summary>
    public StatusItem LastRunStatus { get; set; } = new("Last run", "No sync has run yet.", ItemStatus.Unavailable);

    /// <summary>
    /// Gets or sets the "Sync now" button.
    /// </summary>
    public ButtonItem SyncNowButton { get; set; } = new("Sync now") { Icon = IconNames.sync, Data1 = "SyncNow", IsVisible = false };
}
