using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

[ApiController]
[Route("api/notifications")]
[Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class NotificationsController : ControllerBase
{
    private readonly IOrderNotificationService _service;
    public NotificationsController(IOrderNotificationService service) => _service = service;

    [HttpPost("{notificationId:int}/resend")]
    public async Task<IActionResult> Resend(int notificationId, ResendNotificationRequest request, CancellationToken ct)
    {
        var resultId = await _service.ResendAsync(notificationId, request.IdempotencyKey, ct);
        return Created($"/api/notifications/{resultId}", new { notificationId = resultId });
    }

    [HttpDelete("{notificationId:int}/content")]
    public async Task<IActionResult> DisposeContent(int notificationId, CancellationToken ct) =>
        await _service.DisposeContentAsync(notificationId, ct) ? NoContent() : NotFound();

    [HttpGet("reconciliation")]
    public Task<ReconciliationView> Reconciliation([FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to, CancellationToken ct) => _service.ReconcileAsync(from, to, ct);
}

public sealed record ResendNotificationRequest(string IdempotencyKey);
