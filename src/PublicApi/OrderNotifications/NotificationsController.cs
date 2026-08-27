using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Twilio;

namespace Microsoft.eShopWeb.PublicApi.OrderNotifications;

[ApiController]
[Route("api/notifications")]
[Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class NotificationsController : ControllerBase
{
    private readonly CatalogContext _db;
    private readonly NotificationCoordinator _notifications;
    private readonly ITwilioMessagingClient _messaging;

    public NotificationsController(
        CatalogContext db,
        NotificationCoordinator notifications,
        ITwilioMessagingClient messaging)
    {
        _db = db;
        _notifications = notifications;
        _messaging = messaging;
    }

    [HttpPost("{notificationId:int}/resend")]
    public async Task<IActionResult> Resend(
        int notificationId,
        ResendNotificationRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 128)
        {
            return BadRequest(new { error = "idempotencyKey is required and must be at most 128 characters." });
        }

        try
        {
            var notification = await _notifications.ResendAsync(notificationId, request.IdempotencyKey, cancellationToken);
            return Ok(new { notificationId = notification.Id });
        }
        catch (NotificationNotFoundException)
        {
            return NotFound();
        }
        catch (NotificationConflictException exception)
        {
            return Conflict(new { error = exception.Message });
        }
    }

    [HttpDelete("{notificationId:int}/content")]
    public async Task<IActionResult> DisposeContent(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _db.OrderNotifications.SingleOrDefaultAsync(x => x.Id == notificationId, cancellationToken);
        if (notification == null)
        {
            return NotFound();
        }

        try
        {
            await _notifications.RedactAsync(notification, cancellationToken);
            return NoContent();
        }
        catch (Exception)
        {
            return StatusCode(502, new { error = "The provider could not dispose of the message content." });
        }
    }

    [HttpGet("reconciliation")]
    public async Task<IActionResult> Reconciliation(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        if (from == null || to == null || from > to)
        {
            return BadRequest(new { error = "from and to must be valid ISO-8601 date-times, with from no later than to." });
        }

        try
        {
            var provider = await _messaging.ListAsync(from.Value, to.Value, cancellationToken);
            var local = await _db.OrderNotifications
                .Where(x => x.CreatedAt >= from && x.CreatedAt <= to)
                .OrderBy(x => x.CreatedAt)
                .ToListAsync(cancellationToken);

            var localBySid = local.Where(x => x.ProviderMessageSid != null).ToDictionary(x => x.ProviderMessageSid!);
            foreach (var message in provider)
            {
                if (localBySid.TryGetValue(message.Sid, out var notification))
                {
                    notification.RecordProviderStatus(message.Status, message.ErrorCode, DateTimeOffset.UtcNow);
                }
            }
            await _db.SaveChangesAsync(cancellationToken);

            return Ok(ReconciliationResponse.Build(from.Value, to.Value, local, provider));
        }
        catch (Exception)
        {
            return StatusCode(502, new { error = "The provider reconciliation request failed." });
        }
    }
}

public sealed class ResendNotificationRequest
{
    public string IdempotencyKey { get; set; } = string.Empty;
}
