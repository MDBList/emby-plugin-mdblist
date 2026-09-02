using System;
using Emby.Plugin.MDBList.Api.Models;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace Emby.Plugin.MDBList.Api.Requests;

/// <summary>
/// Triggers a full sync run immediately -- the config page's "Sync now"
/// button.
/// </summary>
[Route("/MDBList/Users/{UserId}/Sync", "POST")]
[Authenticated(Roles = "Admin")]
public class TriggerSync : IReturn<SyncStatusResult>
{
    /// <summary>
    /// Gets or sets the linked Emby user.
    /// </summary>
    public Guid UserId { get; set; }
}
