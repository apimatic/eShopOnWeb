using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Services;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

[ApiController]
[Route("api/notifications")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme,
    Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS)]
public sealed class NotificationsController : ControllerBase
{
    private readonly CatalogContext _db;
    private readonly OrderNotificationService _notifications;
    private readonly ITwilioMessagingClient _twilio;

    public NotificationsController(CatalogContext db, OrderNotificationService notifications,
        ITwilioMessagingClient twilio)
    {
        _db = db;
        _notifications = notifications;
        _twilio = twilio;
    }

    [HttpPost("{notificationId:int}/resend")]
    public async Task<IActionResult> Resend(int notificationId, ResendNotificationRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 128)
        {
            return BadRequest(new { error = "An idempotencyKey of at most 128 characters is required." });
        }

        try
        {
            var result = await _notifications.ResendAsync(notificationId, request.IdempotencyKey,
                cancellationToken);
            return result is null ? NotFound() : Ok(new { notificationId = result.Id });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { error = exception.Message });
        }
    }

    [HttpDelete("{notificationId:int}/content")]
    public async Task<IActionResult> DisposeContent(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _db.OrderNotifications.SingleOrDefaultAsync(x => x.Id == notificationId,
            cancellationToken);
        if (notification is null)
        {
            return NotFound();
        }

        try
        {
            await _notifications.RedactAsync(notification, cancellationToken);
            return NoContent();
        }
        catch (TwilioProviderException)
        {
            return StatusCode((int)HttpStatusCode.BadGateway,
                new { error = "The provider could not confirm content redaction." });
        }
    }

    [HttpGet("reconciliation")]
    public async Task<IActionResult> Reconciliation([FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to, CancellationToken cancellationToken)
    {
        if (!from.HasValue || !to.HasValue || from > to)
        {
            return BadRequest(new { error = "from and to must be valid ISO-8601 date-times and from must not exceed to." });
        }

        IReadOnlyList<ProviderMessage> providerMessages;
        try
        {
            providerMessages = await _twilio.ListMessagesAsync(from.Value, to.Value, cancellationToken);
        }
        catch (TwilioProviderException)
        {
            return StatusCode((int)HttpStatusCode.BadGateway,
                new { error = "The provider reconciliation feed is currently unavailable." });
        }

        var providerSids = providerMessages.Select(x => x.Sid).ToHashSet(StringComparer.Ordinal);
        var local = await _db.OrderNotifications.AsNoTracking()
            .Where(x => (x.CreatedAt >= from.Value && x.CreatedAt <= to.Value) ||
                        (x.ProviderMessageSid != null && providerSids.Contains(x.ProviderMessageSid)))
            .ToListAsync(cancellationToken);
        var localBySid = local.Where(x => x.ProviderMessageSid != null)
            .ToDictionary(x => x.ProviderMessageSid!, StringComparer.Ordinal);
        var rows = new List<object>();

        foreach (var provider in providerMessages)
        {
            localBySid.TryGetValue(provider.Sid, out var match);
            rows.Add(new
            {
                match = match is null ? "ProviderOnly" : "Matched",
                providerMessageId = provider.Sid,
                notificationId = match?.Id,
                providerStatus = provider.Status,
                localStatus = match?.ProviderStatus,
                providerErrorCode = provider.ErrorCode,
                sentAt = provider.DateSent,
                localCreatedAt = match?.CreatedAt
            });
        }

        foreach (var notification in local.Where(x =>
                     x.ProviderMessageSid is null || !providerSids.Contains(x.ProviderMessageSid)))
        {
            rows.Add(new
            {
                match = "LocalOnly",
                providerMessageId = notification.ProviderMessageSid,
                notificationId = (int?)notification.Id,
                providerStatus = (string?)null,
                localStatus = notification.ProviderStatus,
                providerErrorCode = notification.ProviderErrorCode,
                sentAt = (DateTimeOffset?)null,
                localCreatedAt = (DateTimeOffset?)notification.CreatedAt
            });
        }

        return Ok(new { from, to, entries = rows });
    }
}

public sealed class ResendNotificationRequest
{
    public string IdempotencyKey { get; set; } = string.Empty;
}
