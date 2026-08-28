using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

[ApiController]
[Authorize(
    Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[Route("api/notifications")]
public sealed class NotificationsController : ControllerBase
{
    private readonly IOrderNotificationService _service;

    public NotificationsController(IOrderNotificationService service) => _service = service;

    [HttpPost("{notificationId:int}/resend")]
    public async Task<ActionResult<NotificationResendResponse>> Resend(
        int notificationId,
        NotificationResendRequest request,
        CancellationToken cancellationToken)
    {
        var newNotificationId = await _service.ResendNotificationAsync(
            notificationId,
            request.IdempotencyKey,
            cancellationToken);
        return Ok(new NotificationResendResponse(newNotificationId));
    }

    [HttpDelete("{notificationId:int}/content")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DisposeContent(int notificationId, CancellationToken cancellationToken)
    {
        await _service.DisposeNotificationContentAsync(notificationId, cancellationToken);
        return NoContent();
    }

    [HttpGet("reconciliation")]
    public Task<ReconciliationView> Reconciliation(
        [FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to,
        CancellationToken cancellationToken) =>
        _service.ReconcileAsync(from, to, cancellationToken);
}

public sealed record NotificationResendRequest(string IdempotencyKey);
public sealed record NotificationResendResponse(int NotificationId);
