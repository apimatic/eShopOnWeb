using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

[ApiController]
[Route("api/notifications")]
[Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class NotificationsController : ControllerBase
{
    private readonly CatalogContext _context;
    private readonly OrderNotificationService _notifications;

    public NotificationsController(CatalogContext context, OrderNotificationService notifications)
    {
        _context = context;
        _notifications = notifications;
    }

    [HttpPost("{notificationId:int}/resend")]
    public async Task<IActionResult> Resend(int notificationId, ResendNotificationRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 128)
            return BadRequest(new { error = "idempotencyKey is required and cannot exceed 128 characters." });

        var (notification, error) = await _notifications.ResendAsync(notificationId,
            request.IdempotencyKey, cancellationToken);
        return error switch
        {
            null => Ok(new { notificationId = notification!.Id,
                providerMessageId = notification.ProviderMessageId,
                providerStatus = notification.ProviderStatus }),
            "not-found" => NotFound(),
            "not-resendable" => Conflict(new { error = "Only failed or undelivered notifications can be resent." }),
            "content-disposed" => Conflict(new { error = "Disposed message content cannot be resent." }),
            "contact-removed" => Conflict(new { error = "The destination contact number has been removed." }),
            "order-cancelled" => Conflict(new { error = "A delivery follow-up cannot be resent for a cancelled order." }),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    [HttpDelete("{notificationId:int}/content")]
    public async Task<IActionResult> DisposeContent(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _context.OrderNotifications.SingleOrDefaultAsync(x =>
            x.Id == notificationId, cancellationToken);
        if (notification is null) return NotFound();
        if (!await _notifications.DisposeContentAsync(notification, cancellationToken))
            return StatusCode(StatusCodes.Status502BadGateway,
                new { error = "The provider did not confirm content disposal; no local content was removed." });
        return NoContent();
    }

    [HttpGet("reconciliation")]
    public async Task<IActionResult> Reconciliation([FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to, CancellationToken cancellationToken)
    {
        if (from == default || to == default || from > to)
            return BadRequest(new { error = "from and to must be valid ISO-8601 date-times and from must not exceed to." });

        IReadOnlyCollection<ApplicationCore.Interfaces.ProviderMessage> providerMessages;
        try
        {
            providerMessages = await _notifications.ListProviderMessagesAsync(from, to, cancellationToken);
        }
        catch (ApplicationCore.Interfaces.MessageProviderException)
        {
            return StatusCode(StatusCodes.Status502BadGateway,
                new { error = "The provider reconciliation record is temporarily unavailable." });
        }

        var providerIds = providerMessages.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        var localMessages = await _context.OrderNotifications
            .Where(x =>
                (x.ProviderDateSent != null && x.ProviderDateSent >= from && x.ProviderDateSent <= to)
                || (x.ProviderDateSent == null && x.ProviderDateCreated != null
                    && x.ProviderDateCreated >= from && x.ProviderDateCreated <= to)
                || (x.ProviderDateSent == null && x.ProviderDateCreated == null
                    && x.CreatedAt >= from && x.CreatedAt <= to))
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
        var knownProviderIds = localMessages.Where(x => x.ProviderMessageId != null)
            .Select(x => x.ProviderMessageId!).ToHashSet(StringComparer.Ordinal);
        foreach (var idChunk in providerIds.Where(x => !knownProviderIds.Contains(x)).Chunk(500))
        {
            localMessages.AddRange(await _context.OrderNotifications
                .Where(x => x.ProviderMessageId != null && idChunk.Contains(x.ProviderMessageId))
                .ToListAsync(cancellationToken));
        }
        var localByProviderId = localMessages
            .Where(x => x.ProviderMessageId != null)
            .ToDictionary(x => x.ProviderMessageId!, StringComparer.Ordinal);

        var entries = providerMessages.Select(provider =>
        {
            localByProviderId.TryGetValue(provider.Id, out var local);
            return new ReconciliationEntry(provider.Id, local?.Id, local?.OrderId,
                local?.ProviderStatus, provider.Status, provider.ErrorCode,
                provider.DateSent ?? provider.DateCreated, local is null ? "provider-only" : "matched");
        }).Concat(localMessages
            .Where(x => x.ProviderMessageId is null || !providerIds.Contains(x.ProviderMessageId))
            .Select(local => new ReconciliationEntry(local.ProviderMessageId, local.Id, local.OrderId,
                local.ProviderStatus, null, local.ProviderErrorCode, local.CreatedAt, "application-only")))
            .OrderBy(x => x.Timestamp)
            .ToList();

        return Ok(new
        {
            from,
            to,
            matched = entries.Count(x => x.ReconciliationStatus == "matched"),
            providerOnly = entries.Count(x => x.ReconciliationStatus == "provider-only"),
            applicationOnly = entries.Count(x => x.ReconciliationStatus == "application-only"),
            entries
        });
    }
}

public sealed class ResendNotificationRequest
{
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed record ReconciliationEntry(string? ProviderMessageId, int? NotificationId,
    int? OrderId, string? ApplicationStatus, string? ProviderStatus, int? ProviderErrorCode,
    DateTimeOffset? Timestamp, string ReconciliationStatus);
