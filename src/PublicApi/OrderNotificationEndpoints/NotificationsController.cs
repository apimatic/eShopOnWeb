using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

[ApiController]
[Route("api/notifications")]
[Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class NotificationsController : ControllerBase
{
    private readonly IOrderNotificationService _service;
    public NotificationsController(IOrderNotificationService service) => _service = service;

    [HttpPost("{notificationId:int}/resend")]
    public async Task<ActionResult<ResendNotificationResponse>> Resend(int notificationId,
        ResendNotificationRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.ResendAsync(notificationId, request.IdempotencyKey, cancellationToken);
        return result is null
            ? NotFound()
            : Ok(new ResendNotificationResponse(result.NotificationId, result.ProviderMessageId,
                result.ProviderStatus));
    }

    [HttpDelete("{notificationId:int}/content")]
    public async Task<IActionResult> DisposeContent(int notificationId, CancellationToken cancellationToken)
    {
        var result = await _service.DisposeContentAsync(notificationId, cancellationToken);
        return result is null ? NotFound() : NoContent();
    }

    [HttpGet("reconciliation")]
    public Task<ReconciliationResult> Reconciliation([FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to, CancellationToken cancellationToken) =>
        _service.ReconcileAsync(from, to, cancellationToken);
}

public sealed record ResendNotificationRequest(string IdempotencyKey);
public sealed record ResendNotificationResponse(int NotificationId, string? ProviderMessageId,
    string ProviderStatus);
