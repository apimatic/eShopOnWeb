using System;
using System.ComponentModel.DataAnnotations;
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
[Authorize(
    Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class NotificationsController : ControllerBase
{
    private readonly NotificationCoordinator _notifications;

    public NotificationsController(NotificationCoordinator notifications)
    {
        _notifications = notifications;
    }

    [HttpPost("{notificationId:int}/resend")]
    public async Task<IActionResult> Resend(
        int notificationId,
        ResendNotificationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _notifications.ResendAsync(notificationId, request.IdempotencyKey, cancellationToken);
        return result.Outcome switch
        {
            ResendOutcome.Created => Created(
                $"/api/notifications/{result.NotificationId}",
                new { notificationId = result.NotificationId }),
            ResendOutcome.Existing => Ok(new { notificationId = result.NotificationId }),
            ResendOutcome.NotFound => NotFound(),
            ResendOutcome.NotEligible => Conflict(new { message = "Only a failed or undelivered message with retained content and an active contact number can be resent." }),
            _ => Problem(statusCode: StatusCodes.Status502BadGateway, title: "The provider outcome could not be checked.")
        };
    }

    [HttpDelete("{notificationId:int}/content")]
    public async Task<IActionResult> DisposeContent(int notificationId, CancellationToken cancellationToken)
    {
        var outcome = await _notifications.DisposeContentAsync(notificationId, cancellationToken);
        return outcome switch
        {
            ContentDisposalOutcome.Disposed => NoContent(),
            ContentDisposalOutcome.NotFound => NotFound(),
            _ => Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "Content could not be removed from the provider, so the local copy was retained.")
        };
    }

    [HttpGet("reconciliation")]
    public async Task<IActionResult> Reconciliation(
        [FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (from == default || to == default || from > to)
        {
            return BadRequest(new { message = "from and to must be valid ISO-8601 date-times, and from must not be later than to." });
        }

        try
        {
            var entries = await _notifications.ReconcileAsync(from, to, cancellationToken);
            return Ok(new { from, to, entries });
        }
        catch (TwilioProviderException)
        {
            return Problem(statusCode: StatusCodes.Status502BadGateway, title: "Provider reconciliation is unavailable.");
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return Problem(statusCode: StatusCodes.Status502BadGateway, title: "Provider reconciliation is unavailable.");
        }
    }
}

public sealed class ResendNotificationRequest
{
    [Required, StringLength(200, MinimumLength = 1)]
    public string IdempotencyKey { get; init; } = string.Empty;
}
