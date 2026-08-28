using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

[ApiController]
[Route("api/notifications")]
[Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class NotificationsController : ControllerBase
{
    private readonly IOrderNotificationService _notifications;

    public NotificationsController(IOrderNotificationService notifications)
    {
        _notifications = notifications;
    }

    [HttpPost("{notificationId:int}/resend")]
    public async Task<ActionResult<NotificationCreatedResponse>> ResendAsync(int notificationId,
        ResendNotificationRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            return ValidationProblem(title: "An idempotency key is required.");
        var result = await _notifications.ResendAsync(notificationId, request.IdempotencyKey,
            cancellationToken);
        return result switch
        {
            null => NotFound(),
            -1 => Conflict(new ProblemDetails
            {
                Title = "Only failed messages with retained content and an active destination can be resent."
            }),
            _ => Ok(new NotificationCreatedResponse(result.Value))
        };
    }

    [HttpDelete("{notificationId:int}/content")]
    public async Task<IActionResult> DisposeContentAsync(int notificationId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _notifications.DisposeContentAsync(notificationId, cancellationToken)
                ? NoContent()
                : NotFound();
        }
        catch (ProviderRequestException)
        {
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "The provider did not confirm content disposal; local content was retained.");
        }
        catch (HttpRequestException)
        {
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "The provider did not confirm content disposal; local content was retained.");
        }
    }

    [HttpGet("reconciliation")]
    public async Task<ActionResult<ReconciliationResponse>> ReconciliationAsync(
        [FromQuery] DateTimeOffset from, [FromQuery] DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (from == default || to == default || to < from)
            return BadRequest(new ProblemDetails { Title = "A valid ISO-8601 range is required." });
        try
        {
            return Ok(await _notifications.ReconcileAsync(from, to, cancellationToken));
        }
        catch (ProviderRequestException)
        {
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Provider reconciliation is temporarily unavailable.");
        }
        catch (HttpRequestException)
        {
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Provider reconciliation is temporarily unavailable.");
        }
    }
}
