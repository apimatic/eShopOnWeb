using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.OrderNotifications;

[ApiController]
[Route("api/notifications")]
[Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class NotificationsController : ControllerBase
{
    private readonly OrderNotificationService _service;

    public NotificationsController(OrderNotificationService service) => _service = service;

    [HttpPost("{notificationId:int}/resend")]
    [ProducesResponseType(typeof(ResendNotificationResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<ResendNotificationResponse>> Resend(int notificationId,
        ResendNotificationRequest request, CancellationToken cancellationToken)
    {
        var response = await _service.ResendAsync(notificationId, request, cancellationToken);
        return Created($"/api/notifications/{response.NotificationId}", response);
    }

    [HttpDelete("{notificationId:int}/content")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DisposeContent(int notificationId,
        CancellationToken cancellationToken)
    {
        await _service.DisposeContentAsync(notificationId, cancellationToken);
        return NoContent();
    }

    [HttpGet("reconciliation")]
    [ProducesResponseType(typeof(ReconciliationResponse), StatusCodes.Status200OK)]
    public Task<ReconciliationResponse> Reconciliation([FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to, CancellationToken cancellationToken) =>
        _service.ReconcileAsync(from, to, cancellationToken);
}
