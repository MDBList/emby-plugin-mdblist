//------------------------------------------------------------------------------
// Hand-written in the shape the standard .resx designer emits. Emby's Plugin UI
// localization attributes take a (key, typeof(Resources)) pair and resolve the
// value through MediaBrowser.Model's LocalizableString, which requires the
// resource type to be public and each key to be a public static string property
// -- hence the explicit "public" members below. Kept as a plain (non-static)
// class so it also works as a ResourceType anchor.
//------------------------------------------------------------------------------
using System.Globalization;
using System.Resources;

namespace Emby.Plugin.MDBList;

/// <summary>
/// Strongly-typed access to the plugin's UI strings. Backed by
/// Resources.resx (English, neutral) and Resources.zh-Hans.resx.
/// </summary>
public class Resources
{
    private static ResourceManager? _resourceManager;

    /// <summary>
    /// Gets the <see cref="System.Resources.ResourceManager"/> for this class.
    /// </summary>
    public static ResourceManager ResourceManager =>
        _resourceManager ??= new ResourceManager("Emby.Plugin.MDBList.Resources", typeof(Resources).Assembly);

    /// <summary>
    /// Gets or sets the culture used for lookups. Null (the default) means
    /// <see cref="CultureInfo.CurrentUICulture"/>.
    /// </summary>
    public static CultureInfo? Culture { get; set; }

    private static string Get(string name) => ResourceManager.GetString(name, Culture) ?? name;

    public static string Settings_Description => Get(nameof(Settings_Description));

    public static string User_Name => Get(nameof(User_Name));

    public static string User_Desc => Get(nameof(User_Desc));

    public static string Status_Connection => Get(nameof(Status_Connection));

    public static string Status_NotConnected => Get(nameof(Status_NotConnected));

    public static string Status_LastRun => Get(nameof(Status_LastRun));

    public static string Status_NoSyncRun => Get(nameof(Status_NoSyncRun));

    public static string Caption_WhatToSync => Get(nameof(Caption_WhatToSync));

    public static string Caption_Status => Get(nameof(Caption_Status));

    public static string Button_Connect => Get(nameof(Button_Connect));

    public static string Button_Disconnect => Get(nameof(Button_Disconnect));

    public static string Button_DisconnectPrompt => Get(nameof(Button_DisconnectPrompt));

    public static string Button_Test => Get(nameof(Button_Test));

    public static string Button_SyncNow => Get(nameof(Button_SyncNow));

    public static string Opt_Watched_Name => Get(nameof(Opt_Watched_Name));

    public static string Opt_Watched_Desc => Get(nameof(Opt_Watched_Desc));

    public static string Opt_Ratings_Name => Get(nameof(Opt_Ratings_Name));

    public static string Opt_Ratings_Desc => Get(nameof(Opt_Ratings_Desc));

    public static string Opt_Collection_Name => Get(nameof(Opt_Collection_Name));

    public static string Opt_Collection_Desc => Get(nameof(Opt_Collection_Desc));

    public static string Opt_Scrobbling_Name => Get(nameof(Opt_Scrobbling_Name));

    public static string Opt_Scrobbling_Desc => Get(nameof(Opt_Scrobbling_Desc));

    public static string Opt_IgnoreThumb_Name => Get(nameof(Opt_IgnoreThumb_Name));

    public static string Opt_IgnoreThumb_Desc => Get(nameof(Opt_IgnoreThumb_Desc));

    public static string Opt_SyncAfterScan_Name => Get(nameof(Opt_SyncAfterScan_Name));

    public static string Opt_SyncAfterScan_Desc => Get(nameof(Opt_SyncAfterScan_Desc));

    public static string Msg_Connected => Get(nameof(Msg_Connected));

    public static string Msg_RequestingCode => Get(nameof(Msg_RequestingCode));

    public static string Msg_EnterCode => Get(nameof(Msg_EnterCode));

    public static string Msg_AuthDenied => Get(nameof(Msg_AuthDenied));

    public static string Msg_AuthExpired => Get(nameof(Msg_AuthExpired));

    public static string Msg_ConnectionFailed => Get(nameof(Msg_ConnectionFailed));

    public static string Msg_Disconnecting => Get(nameof(Msg_Disconnecting));

    public static string Msg_Testing => Get(nameof(Msg_Testing));

    public static string Msg_TestOk => Get(nameof(Msg_TestOk));

    public static string Msg_TestFailed => Get(nameof(Msg_TestFailed));

    public static string Msg_Syncing => Get(nameof(Msg_Syncing));

    public static string Task_Sync_Name => Get(nameof(Task_Sync_Name));

    public static string Task_Sync_Desc => Get(nameof(Task_Sync_Desc));

    public static string Task_Activity_Name => Get(nameof(Task_Activity_Name));

    public static string Task_Activity_Desc => Get(nameof(Task_Activity_Desc));

    public static string Summary_Watched => Get(nameof(Summary_Watched));

    public static string Summary_Ratings => Get(nameof(Summary_Ratings));

    public static string Summary_Collection => Get(nameof(Summary_Collection));

    public static string Summary_PushSkipped => Get(nameof(Summary_PushSkipped));

    public static string Summary_PullSkipped => Get(nameof(Summary_PullSkipped));

    public static string Summary_WatchedSkipped => Get(nameof(Summary_WatchedSkipped));

    public static string Summary_RatingsSkipped => Get(nameof(Summary_RatingsSkipped));

    public static string Summary_CollectionSkipped => Get(nameof(Summary_CollectionSkipped));

    public static string Summary_EmptyLibrary => Get(nameof(Summary_EmptyLibrary));

    public static string Summary_Failed => Get(nameof(Summary_Failed));

    public static string Summary_Mode_Full => Get(nameof(Summary_Mode_Full));

    public static string Summary_Mode_Incremental => Get(nameof(Summary_Mode_Incremental));

    public static string Summary_PullOnly_Watched => Get(nameof(Summary_PullOnly_Watched));

    public static string Summary_PullOnly_Ratings => Get(nameof(Summary_PullOnly_Ratings));

    public static string Summary_ActivityPrefix => Get(nameof(Summary_ActivityPrefix));

    public static string Summary_Separator => Get(nameof(Summary_Separator));
}
