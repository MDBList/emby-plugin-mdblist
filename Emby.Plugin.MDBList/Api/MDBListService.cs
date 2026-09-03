using System;
using System.Linq;
using System.Threading;
using Emby.Plugin.MDBList.Api.Models;
using Emby.Plugin.MDBList.Api.Requests;
using Emby.Plugin.MDBList.Sync;
using MediaBrowser.Model.Services;

namespace Emby.Plugin.MDBList.Api;

/// <summary>
/// Endpoints backing the plugin's config page: device-code OAuth flow, a
/// connectivity test, manual "Sync now", and the last-run status. Port of
/// jellyfin-plugin-mdblist's MDBListController.cs, rewritten for Emby's
/// ServiceStack-based API model (auto-discovered via <see cref="IService"/>
/// -- no explicit route registration needed) rather than ASP.NET Core MVC.
/// All actions require an elevated (admin) caller via each request DTO's
/// <c>[Authenticated(Roles = "Admin")]</c> attribute. Every method here is
/// synchronous, blocking on the still-genuinely-async work underneath --
/// confirmed live in Phase 1 that async Task-returning IService methods
/// silently never dispatch on Emby's ServiceStack fork.
/// </summary>
public class MDBListService : IService
{
    private readonly OAuthService _oauthService;
    private readonly SyncOrchestrator _orchestrator;
    private readonly SyncStateStore _stateStore;

    /// <summary>
    /// Initializes a new instance of the <see cref="MDBListService"/> class.
    /// </summary>
    /// <param name="oauthService">Instance of the <see cref="OAuthService"/>.</param>
    /// <param name="orchestrator">Instance of the <see cref="SyncOrchestrator"/>.</param>
    /// <param name="stateStore">Instance of the <see cref="SyncStateStore"/>.</param>
    public MDBListService(OAuthService oauthService, SyncOrchestrator orchestrator, SyncStateStore stateStore)
    {
        _oauthService = oauthService;
        _orchestrator = orchestrator;
        _stateStore = stateStore;
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

    /// <summary>
    /// Handles <see cref="TriggerSync"/> -- runs a full sync immediately and
    /// synchronously. Trivial here: this runs in the same process as
    /// everything else, so there's no cross-process signaling to do, unlike
    /// Kodi's addon-process-vs-service split.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <returns>The resulting status, including the run's summary.</returns>
    public object Post(TriggerSync request)
    {
        _orchestrator.RunAsync(request.UserId, CancellationToken.None).GetAwaiter().GetResult();
        return BuildStatus(request.UserId);
    }

    /// <summary>
    /// Handles <see cref="GetStatus"/>.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <returns>The current status.</returns>
    public object Get(GetStatus request)
    {
        return BuildStatus(request.UserId);
    }

    private SyncStatusResult BuildStatus(Guid userId)
    {
        var linkedUserConfig = Plugin.Instance?.Configuration.Users.FirstOrDefault(u => u.EmbyUserId == userId);
        var summary = _stateStore.GetLastRunSummaryAsync(userId, CancellationToken.None).GetAwaiter().GetResult();
        return new SyncStatusResult
        {
            Connected = !string.IsNullOrEmpty(linkedUserConfig?.AccessToken),
            LastRunSummary = summary,
        };
    }
}
