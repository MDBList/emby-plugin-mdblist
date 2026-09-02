using System;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace Emby.Plugin.MDBList.Api.Requests;

/// <summary>
/// Revokes and clears the stored token for a user.
/// </summary>
[Route("/MDBList/Users/{UserId}/Disconnect", "POST")]
[Authenticated(Roles = "Admin")]
public class Disconnect : IReturnVoid
{
    /// <summary>
    /// Gets or sets the Emby user to disconnect.
    /// </summary>
    public Guid UserId { get; set; }
}
