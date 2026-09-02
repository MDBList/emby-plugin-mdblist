using System;
using Emby.Plugin.MDBList.Api.Models;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace Emby.Plugin.MDBList.Api.Requests;

/// <summary>
/// Makes one poll attempt against MDBList's token endpoint.
/// </summary>
[Route("/MDBList/Users/{UserId}/Poll", "POST")]
[Authenticated(Roles = "Admin")]
public class PollDeviceCode : IReturn<PollResult>
{
    /// <summary>
    /// Gets or sets the Emby user to link on success.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets the device code to poll for.
    /// </summary>
    public string DeviceCode { get; set; } = string.Empty;
}
