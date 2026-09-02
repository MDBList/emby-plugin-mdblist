using System;
using Emby.Plugin.MDBList.Api.Models;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace Emby.Plugin.MDBList.Api.Requests;

/// <summary>
/// Gets the linked/connected state and the most recent sync run's summary.
/// </summary>
[Route("/MDBList/Users/{UserId}/Status", "GET")]
[Authenticated(Roles = "Admin")]
public class GetStatus : IReturn<SyncStatusResult>
{
    /// <summary>
    /// Gets or sets the Emby user.
    /// </summary>
    public Guid UserId { get; set; }
}
