using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

[ApiController]
[Route("api/notifications")]
[Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class NotificationsController : ControllerBase
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ResendLocks = new();
    private static readonly HashSet<string> ResendableStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "failed", "undelivered", "canceled", "failed_to_submit"
    };

    private readonly CatalogContext _db;
    private readonly ITwilioGateway _twilio;
    private readonly NotificationCoordinator _notifications;

    public NotificationsController(CatalogContext db, ITwilioGateway twilio,
        NotificationCoordinator notifications)
    {
        _db = db;
        _twilio = twilio;
        _notifications = notifications;
    }

    [HttpPost("{notificationId:int}/resend")]
    public async Task<IActionResult> Resend(int notificationId, ResendNotificationRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 200)
            return BadRequest(new { error = "idempotencyKey is required and must not exceed 200 characters." });

        var lockKey = $"{notificationId}:{request.IdempotencyKey}";
        var gate = ResendLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var existing = await _db.OrderNotifications.SingleOrDefaultAsync(x =>
                x.ResendsNotificationId == notificationId && x.IdempotencyKey == request.IdempotencyKey,
                cancellationToken);
            if (existing is not null) return Ok(new { notificationId = existing.Id });

            var original = await _db.OrderNotifications.SingleOrDefaultAsync(x => x.Id == notificationId,
                cancellationToken);
            if (original is null) return NotFound();
            await _notifications.RefreshAsync(new[] { original }, cancellationToken);
            if (!ResendableStatuses.Contains(original.ProviderStatus))
                return Conflict(new { error = "Only a notification that did not reach the shopper can be resent." });
            if (original.ContentDisposedAt.HasValue || string.IsNullOrWhiteSpace(original.Body))
                return Conflict(new { error = "Disposed notification content cannot be resent." });
            if (!original.ContactNumberId.HasValue || !await _db.ContactNumbers.AnyAsync(x =>
                    x.Id == original.ContactNumberId.Value && x.BuyerId == original.BuyerId,
                    cancellationToken))
                return Conflict(new { error = "The destination is no longer registered and may not be messaged." });

            var resend = new OrderNotification(original.OrderId, original.BuyerId,
                original.ContactNumberId, original.Destination, NotificationKind.Resend,
                original.Body, DateTimeOffset.UtcNow, original.Id, request.IdempotencyKey);
            _db.OrderNotifications.Add(resend);
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                _db.Entry(resend).State = EntityState.Detached;
                existing = await _db.OrderNotifications.SingleOrDefaultAsync(x =>
                    x.ResendsNotificationId == notificationId && x.IdempotencyKey == request.IdempotencyKey,
                    cancellationToken);
                if (existing is not null) return Ok(new { notificationId = existing.Id });
                throw;
            }
            await _notifications.SubmitAsync(resend, null, cancellationToken);
            return Ok(new { notificationId = resend.Id });
        }
        finally
        {
            gate.Release();
        }
    }

    [HttpDelete("{notificationId:int}/content")]
    public async Task<IActionResult> DisposeContent(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _db.OrderNotifications.SingleOrDefaultAsync(x => x.Id == notificationId,
            cancellationToken);
        if (notification is null) return NotFound();
        if (notification.ContentDisposedAt.HasValue) return NoContent();

        if (!string.IsNullOrWhiteSpace(notification.ProviderMessageSid))
        {
            try
            {
                await _twilio.RedactMessageContentAsync(notification.ProviderMessageSid, cancellationToken);
            }
            catch (Exception ex) when (ex is TwilioProviderException or HttpRequestException or TaskCanceledException)
            {
                return Problem(statusCode: 502, title: "Twilio did not confirm content disposal.");
            }
        }

        notification.MarkContentDisposed(DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("reconciliation")]
    public async Task<IActionResult> Reconciliation([FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to, CancellationToken cancellationToken)
    {
        if (from == default || to == default || from > to)
            return BadRequest(new { error = "from and to must be valid ISO-8601 date-times and from must not exceed to." });

        IReadOnlyList<ProviderMessage> providerMessages;
        try
        {
            providerMessages = await _twilio.ListMessagesAsync(from, to, cancellationToken);
        }
        catch (Exception ex) when (ex is TwilioProviderException or HttpRequestException or TaskCanceledException)
        {
            return Problem(statusCode: 502, title: "Twilio reconciliation could not be completed.");
        }

        var local = await _db.OrderNotifications.AsNoTracking()
            .Where(x => x.CreatedAt >= from && x.CreatedAt <= to).ToListAsync(cancellationToken);
        var localBySid = local.Where(x => x.ProviderMessageSid != null)
            .ToDictionary(x => x.ProviderMessageSid!, StringComparer.Ordinal);
        var providerBySid = providerMessages.Where(x => x.DateSent >= from && x.DateSent <= to)
            .GroupBy(x => x.Sid).ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
        var entries = new List<ReconciliationEntry>();

        foreach (var provider in providerBySid.Values)
        {
            localBySid.TryGetValue(provider.Sid, out var app);
            entries.Add(new ReconciliationEntry(provider.Sid, app?.Id,
                app is null ? "provider_only" : "matched", app?.ProviderStatus,
                provider.Status, app?.CreatedAt, provider.DateSent));
        }
        foreach (var app in local.Where(x => x.ProviderMessageSid is null || !providerBySid.ContainsKey(x.ProviderMessageSid)))
        {
            entries.Add(new ReconciliationEntry(app.ProviderMessageSid, app.Id, "application_only",
                app.ProviderStatus, null, app.CreatedAt, null));
        }

        return Ok(new
        {
            from,
            to,
            entries = entries.OrderBy(x => x.ApplicationCreatedAt ?? x.ProviderDateSent).ToArray(),
            matched = entries.Count(x => x.Match == "matched"),
            providerOnly = entries.Count(x => x.Match == "provider_only"),
            applicationOnly = entries.Count(x => x.Match == "application_only")
        });
    }
}

public sealed class ResendNotificationRequest
{
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed record ReconciliationEntry(string? ProviderMessageSid, int? NotificationId,
    string Match, string? ApplicationStatus, string? ProviderStatus,
    DateTimeOffset? ApplicationCreatedAt, DateTimeOffset? ProviderDateSent);
