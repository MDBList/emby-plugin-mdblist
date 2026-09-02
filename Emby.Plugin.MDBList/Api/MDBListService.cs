using System.Threading;
using Emby.Plugin.MDBList.Api.Models;
using Emby.Plugin.MDBList.Api.Requests;
using MediaBrowser.Model.Services;

namespace Emby.Plugin.MDBList.Api;

/// <summary>
/// Endpoints backing the plugin's config page: device-code OAuth flow and a
/// connectivity test. Port of jellyfin-plugin-mdblist's MDBListController.cs,
/// rewritten for Emby's ServiceStack-based API model (auto-discovered via
/// <see cref="IService"/> -- no explicit route registration needed) rather
/// than ASP.NET Core MVC. All actions require an elevated (admin) caller via
/// each request DTO's <c>[Authenticated(Roles = "Admin")]</c> attribute.
/// </summary>
public class MDBListService : IService
{
    private readonly OAuthService _oauthService;

    /// <summary>
    /// Initializes a new instance of the <see cref="MDBListService"/> class.
    /// </summary>
    /// <param name="oauthService">Instance of the <see cref="OAuthService"/>.</param>
    public MDBListService(OAuthService oauthService)
    {
        _oauthService = oauthService;
    }

    /// <summary>
    /// Handles <see cref="GetDeviceCode"/>.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <returns>The device/user codes and verification URI.</returns>
    public object Post(GetDeviceCode request)
    {
        return _oauthService.StartDeviceAuthorizationAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Handles <see cref="PollDeviceCode"/>.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <returns>The poll status.</returns>
    public object Post(PollDeviceCode request)
    {
        return _oauthService.PollTokenAsync(request.UserId, request.DeviceCode, CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Handles <see cref="Disconnect"/>.
    /// </summary>
    /// <param name="request">The request.</param>
    public void Post(Disconnect request)
    {
        _oauthService.DisconnectAsync(request.UserId, CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Handles <see cref="TestConnection"/>.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <returns>The server's own watermark timestamp.</returns>
    public object Post(TestConnection request)
    {
        var serverTime = _oauthService.TestConnectionAsync(request.UserId, CancellationToken.None).GetAwaiter().GetResult();
        return new ConnectionTestResult { ServerTime = serverTime };
    }
}
