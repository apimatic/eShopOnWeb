using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Notifications;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

[ApiController]
[Route("api/notifications")]
[Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class NotificationsController : ControllerBase
{
    private readonly OrderNotificationService _service;

    public NotificationsController(OrderNotificationService service) => _service = service;

    [HttpPost("{notificationId:int}/resend")]
    public async Task<IActionResult> Resend(int notificationId, ResendNotificationRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var notification = await _service.ResendAsync(notificationId, request.IdempotencyKey, cancellationToken);
            return notification is null ? NotFound() : Ok(new { notificationId = notification.Id });
        }
        catch (WorkflowValidationException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (WorkflowConflictException exception)
        {
            return Conflict(new { error = exception.Message });
        }
    }

    [HttpDelete("{notificationId:int}/content")]
    public async Task<IActionResult> DisposeContent(int notificationId, CancellationToken cancellationToken)
    {
        try
        {
            return await _service.DisposeContentAsync(notificationId, cancellationToken) ? NoContent() : NotFound();
        }
        catch (TwilioProviderException)
        {
            return StatusCode(StatusCodes.Status502BadGateway,
                new { error = "The content was not disposed because provider redaction did not complete." });
        }
    }

    [HttpGet("reconciliation")]
    public async Task<IActionResult> Reconciliation([FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        if (!from.HasValue || !to.HasValue)
        {
            return BadRequest(new { error = "Both from and to ISO-8601 date-times are required." });
        }

        try
        {
            return Ok(await _service.ReconcileAsync(from.Value, to.Value, cancellationToken));
        }
        catch (WorkflowValidationException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (TwilioProviderException)
        {
            return StatusCode(StatusCodes.Status502BadGateway,
                new { error = "Provider reconciliation is temporarily unavailable." });
        }
    }
}

public sealed class ResendNotificationRequest
{
    public string IdempotencyKey { get; set; } = string.Empty;
}
