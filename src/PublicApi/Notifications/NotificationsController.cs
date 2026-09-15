using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

[ApiController]
[Route("api/notifications")]
[Authorize(
    Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class NotificationsController : ControllerBase
{
    private readonly NotificationWorkflowService _workflow;

    public NotificationsController(NotificationWorkflowService workflow)
    {
        _workflow = workflow;
    }

    [HttpPost("{notificationId:int}/resend")]
    [ProducesResponseType(typeof(ResendNotificationResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<ResendNotificationResponse>> Resend(
        int notificationId,
        ResendNotificationRequest request,
        CancellationToken cancellationToken)
    {
        var notification = await _workflow.ResendAsync(notificationId, request.IdempotencyKey, cancellationToken);
        return Created(
            $"/api/orders/{notification.OrderId}/notifications",
            new ResendNotificationResponse(notification.Id));
    }

    [HttpDelete("{notificationId:int}/content")]
    public async Task<IActionResult> DisposeContent(int notificationId, CancellationToken cancellationToken)
    {
        return await _workflow.DisposeContentAsync(notificationId, cancellationToken)
            ? NoContent()
            : NotFound();
    }

    [HttpGet("reconciliation")]
    public async Task<ActionResult<ReconciliationResponse>> Reconcile(
        [FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var messages = await _workflow.ReconcileAsync(from, to, cancellationToken);
        return Ok(ReconciliationResponse.Create(from, to, messages));
    }
}
