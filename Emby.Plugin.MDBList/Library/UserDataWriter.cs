using System;
using System.Threading;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;

namespace Emby.Plugin.MDBList.Library;

/// <summary>
/// Saves user data so one item Emby can't save doesn't fail a whole sync:
/// the item is logged and skipped, and the rest of the sync carries on.
/// </summary>
internal static class UserDataWriter
{
    /// <summary>
    /// Saves <paramref name="userData"/> with reason Import, logging and
    /// skipping the item if Emby can't save it.
    /// </summary>
    /// <param name="userDataManager">Emby's user data manager.</param>
    /// <param name="logger">Where to log an item that couldn't be saved.</param>
    /// <param name="user">The user.</param>
    /// <param name="item">The item.</param>
    /// <param name="userData">The user data to save.</param>
    /// <returns>True if saved.</returns>
    public static bool TrySave(IUserDataManager userDataManager, ILogger logger, User user, BaseItem item, UserItemData userData)
    {
        try
        {
            userDataManager.SaveUserData(user, item, userData, UserDataSaveReason.Import, CancellationToken.None);
            return true;
        }
        catch (Exception ex) when (ex is not ObjectDisposedException)
        {
            // Emby's server is closed source, so which exceptions a save can
            // throw isn't known: any of them skips just this item. Not
            // ObjectDisposedException: that's Emby shutting down, which should
            // end the sync, not log every remaining item as unsaveable.
            logger.ErrorException(
                "MDBList Sync: Emby couldn't save user data for {0} ({1}, user data key {2}), skipped",
                ex,
                item.Name,
                item.InternalId,
                item.UserDataKey);
            return false;
        }
    }
}
