using System;
using Emby.Plugin.MDBList.Api.Models;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace Emby.Plugin.MDBList.Api.Requests;

/// <summary>
/// Tests connectivity by calling /sync/last_activities.
/// </summary>
[Route("/MDBList/Users/{UserId}/Test", "POST")]
[Authenticated(Roles = "Admin")]
public class TestConnection : IReturn<ConnectionTestResult>
{
    /// <summary>
    /// Gets or sets the linked Emby user.
    /// </summary>
    public Guid UserId { get; set; }
}
