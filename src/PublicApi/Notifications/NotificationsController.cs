using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Services;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

[ApiController]
[Route("api/notifications")]
[Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class NotificationsController : ControllerBase
{
    private readonly CatalogContext _db;
    private readonly IMessagingProvider _provider;
    private readonly OrderNotificationDispatcher _notifications;

    public NotificationsController(CatalogContext db, IMessagingProvider provider,
        OrderNotificationDispatcher notifications)
    {
        _db = db;
        _provider = provider;
        _notifications = notifications;
    }

    [HttpPost("{notificationId:int}/resend")]
    public async Task<ActionResult<ResendNotificationResponse>> Resend(int notificationId,
        ResendNotificationRequest request, CancellationToken cancellationToken)
    {
        var original = await _db.OrderNotifications.SingleOrDefaultAsync(x => x.Id == notificationId,
            cancellationToken);
        if (original is null)
        {
            return NotFound();
        }

        if (original.ProviderMessageSid is not null)
        {
            await _notifications.RefreshAsync(new[] { original }, cancellationToken);
        }

        if (original.ProviderStatus is not ("failed" or "undelivered" or "provider-rejected"))
        {
            return Conflict(new ProblemDetails { Detail = "Only a failed or undelivered notification can be resent." });
        }

        if (original.ContentRedacted || string.IsNullOrEmpty(original.Body))
        {
            return Conflict(new ProblemDetails { Detail = "A notification whose content was disposed cannot be resent." });
        }

        var contact = await _db.ContactNumbers.SingleOrDefaultAsync(
            x => x.Id == original.ContactNumberId && x.RemovedAt == null, cancellationToken);
        if (contact is null)
        {
            return Conflict(new ProblemDetails { Detail = "The destination is no longer registered." });
        }

        var resend = await _notifications.ResendAsync(original, contact, request.IdempotencyKey, cancellationToken);
        return Ok(new ResendNotificationResponse(resend.Id));
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

        if (notification.ContentRedacted)
        {
            return NoContent();
        }

        if (notification.ProviderMessageSid is not null)
        {
            try
            {
                var providerMessage = await _provider.RedactContentAsync(notification.ProviderMessageSid,
                    cancellationToken);
                notification.RefreshProviderState(providerMessage, DateTimeOffset.UtcNow);
            }
            catch (MessagingProviderException ex)
            {
                return Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
            }
        }

        notification.Redact();
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("reconciliation")]
    public async Task<ActionResult<ReconciliationResponse>> Reconciliation(
        [FromQuery, Required] DateTimeOffset? from,
        [FromQuery, Required] DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        if (!from.HasValue || !to.HasValue || from > to)
        {
            ModelState.AddModelError(nameof(from), "from must be earlier than or equal to to.");
            return ValidationProblem(ModelState);
        }

        IReadOnlyList<ProviderMessage> providerMessages;
        try
        {
            providerMessages = await _provider.ListAsync(from.Value, to.Value, cancellationToken);
        }
        catch (MessagingProviderException ex)
        {
            return Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
        }

        var local = await _db.OrderNotifications
            .Where(x => x.CreatedAt >= from.Value && x.CreatedAt <= to.Value)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        var localProviderIds = local.Where(x => x.ProviderMessageSid is not null)
            .Select(x => x.ProviderMessageSid!).ToHashSet(StringComparer.Ordinal);
        var providerIdsToMatch = providerMessages.Select(x => x.Sid)
            .Where(x => !localProviderIds.Contains(x)).Distinct(StringComparer.Ordinal).ToArray();

        foreach (var providerIdBatch in providerIdsToMatch.Chunk(500))
        {
            var matchesOutsideLocalRange = await _db.OrderNotifications
                .Where(x => x.ProviderMessageSid != null && providerIdBatch.Contains(x.ProviderMessageSid))
                .ToListAsync(cancellationToken);
            local.AddRange(matchesOutsideLocalRange);
        }

        var localBySid = local.Where(x => x.ProviderMessageSid != null)
            .ToDictionary(x => x.ProviderMessageSid!, x => x);
        var entries = new List<ReconciliationEntry>();

        foreach (var message in providerMessages)
        {
            localBySid.TryGetValue(message.Sid, out var match);
            entries.Add(new ReconciliationEntry(message.Sid, match?.Id, match is not null, true,
                match?.ProviderStatus, message.Status, match?.CreatedAt, message.DateCreated, message.DateSent));
            localBySid.Remove(message.Sid);
        }

        entries.AddRange(local.Where(x => x.ProviderMessageSid is null || localBySid.ContainsKey(x.ProviderMessageSid))
            .Select(x => new ReconciliationEntry(x.ProviderMessageSid, x.Id, true, false,
                x.ProviderStatus, null, x.CreatedAt, null, null)));

        return Ok(new ReconciliationResponse(from.Value, to.Value, entries));
    }
}

public sealed class ResendNotificationRequest
{
    [Required, StringLength(200, MinimumLength = 1)]
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed record ResendNotificationResponse(int NotificationId);
public sealed record ReconciliationResponse(DateTimeOffset From, DateTimeOffset To,
    IReadOnlyList<ReconciliationEntry> Entries);
public sealed record ReconciliationEntry(string? ProviderMessageId, int? NotificationId,
    bool InApplication, bool AtProvider, string? ApplicationStatus, string? ProviderStatus,
    DateTimeOffset? ApplicationCreatedAt, DateTimeOffset? ProviderCreatedAt, DateTimeOffset? ProviderSentAt);
