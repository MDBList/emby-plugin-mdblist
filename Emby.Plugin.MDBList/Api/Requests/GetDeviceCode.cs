using System;
using Emby.Plugin.MDBList.Api.Models;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace Emby.Plugin.MDBList.Api.Requests;

/// <summary>
/// Starts a device-authorization flow for the given user.
/// </summary>
[Route("/MDBList/Users/{UserId}/DeviceCode", "POST")]
[Authenticated(Roles = "Admin")]
public class GetDeviceCode : IReturn<DeviceCodeResult>
{
    /// <summary>
    /// Gets or sets the Emby user to link.
    /// </summary>
    public Guid UserId { get; set; }
}
