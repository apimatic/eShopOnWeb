using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class NotificationAdminService : INotificationAdminService
{
    private const int MaxReconciliationPages = 50;

    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<ResendIdempotencyRecord> _idempotency;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly ISmsGateway _smsGateway;
    private readonly IAppLogger<NotificationAdminService> _logger;

    public NotificationAdminService(
        IRepository<OrderNotification> notifications,
        IRepository<ResendIdempotencyRecord> idempotency,
        IRepository<ContactNumber> contactNumbers,
        ISmsGateway smsGateway,
        IAppLogger<NotificationAdminService> logger)
    {
        _notifications = notifications;
        _idempotency = idempotency;
        _contactNumbers = contactNumbers;
        _smsGateway = smsGateway;
        _logger = logger;
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new InvalidOperationException("An idempotency key is required.");
        }

        var existing = await _idempotency.FirstOrDefaultAsync(
            new ResendIdempotencyByKeySpec(idempotencyKey, notificationId), ct);
        if (existing is not null)
        {
            var prior = await _notifications.GetByIdAsync(existing.ResultNotificationId, ct);
            if (prior is not null)
            {
                return prior;
            }
        }

        var source = await _notifications.GetByIdAsync(notificationId, ct)
            ?? throw new NotificationNotFoundException("Notification was not found.");

        if (!string.IsNullOrEmpty(source.ProviderSid))
        {
            try
            {
                var latest = await _smsGateway.FetchAsync(source.ProviderSid, ct);
                source.ApplyProviderState(latest.Sid, latest.Status, latest.ErrorCode, latest.ErrorMessage, source.ContentRedacted ? source.Body : latest.Body);
                await _notifications.UpdateAsync(source, ct);
            }
            catch (Exception)
            {
                _logger.LogWarning("Failed to refresh notification {NotificationId} before resend.", notificationId);
            }
        }

        if (!source.DidNotReachShopper)
        {
            throw new NotificationNotResendableException("Only messages that did not reach the shopper can be resent.");
        }

        var destination = await ResolveResendDestinationAsync(source, ct);
        var body = source.Body;
        if (string.IsNullOrEmpty(body))
        {
            throw new NotificationNotResendableException("The original message content is no longer available.");
        }

        ProviderMessage result;
        try
        {
            result = await _smsGateway.SendAsync(destination, body, sendAt: null, ct);
        }
        catch (Exception)
        {
            _logger.LogWarning("Resend threw for notification {NotificationId}.", notificationId);
            result = new ProviderMessage(false, null, "send_failed", null, null, null, destination, null, DateTimeOffset.UtcNow);
        }

        var created = new OrderNotification(
            source.OrderId,
            source.BuyerId,
            NotificationKind.Resend,
            destination,
            body,
            result.Sid,
            result.Status,
            result.ErrorCode,
            result.ErrorMessage,
            scheduledSendAt: null);

        created = await _notifications.AddAsync(created, ct);

        await _idempotency.AddAsync(new ResendIdempotencyRecord(idempotencyKey, notificationId, created.Id), ct);

        return created;
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken ct)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, ct)
            ?? throw new NotificationNotFoundException("Notification was not found.");

        if (!string.IsNullOrEmpty(notification.ProviderSid))
        {
            var updated = await _smsGateway.RedactBodyAsync(notification.ProviderSid, ct);
            notification.ApplyProviderState(updated.Sid, updated.Status, updated.ErrorCode, updated.ErrorMessage, body: null);
        }

        notification.MarkContentRedacted();
        await _notifications.UpdateAsync(notification, ct);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        if (to < from)
        {
            throw new InvalidOperationException("The 'to' timestamp must be on or after 'from'.");
        }

        var providerMessages = new List<ProviderMessage>();
        string? pageToken = null;
        var pages = 0;
        var truncated = false;

        do
        {
            var page = await _smsGateway.ListSentFromAsync(from, to, pageToken, ct);
            providerMessages.AddRange(page.Messages);
            pageToken = page.NextPageToken;
            pages++;
            if (pages >= MaxReconciliationPages && pageToken is not null)
            {
                truncated = true;
                break;
            }
        } while (!string.IsNullOrEmpty(pageToken));

        var local = await _notifications.ListAsync(new NotificationsWithProviderSidInRangeSpec(from, to), ct);
        var localBySid = local
            .Where(n => !string.IsNullOrEmpty(n.ProviderSid))
            .GroupBy(n => n.ProviderSid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid))
            .GroupBy(m => m.Sid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var matched = new List<ReconciliationRow>();
        var providerOnly = new List<ReconciliationRow>();
        var eshopOnly = new List<ReconciliationRow>();

        foreach (var (sid, provider) in providerBySid)
        {
            if (localBySid.TryGetValue(sid, out var localRow))
            {
                matched.Add(new ReconciliationRow(localRow.Id.ToString(), sid, provider.Status, localRow.Kind.ToString(), "matched"));
            }
            else
            {
                providerOnly.Add(new ReconciliationRow(null, sid, provider.Status, null, "providerOnly"));
            }
        }

        foreach (var (sid, localRow) in localBySid)
        {
            if (!providerBySid.ContainsKey(sid))
            {
                eshopOnly.Add(new ReconciliationRow(localRow.Id.ToString(), sid, localRow.Status, localRow.Kind.ToString(), "eshopOnly"));
            }
        }

        return new ReconciliationReport(from, to, truncated, matched, providerOnly, eshopOnly);
    }

    private async Task<string> ResolveResendDestinationAsync(OrderNotification source, CancellationToken ct)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpec(source.BuyerId), ct);
        var stillRegistered = numbers.Any(n => n.CanonicalNumber == source.Destination);
        if (stillRegistered)
        {
            return source.Destination;
        }

        var replacement = numbers.FirstOrDefault()?.CanonicalNumber;
        if (replacement is null)
        {
            throw new NotificationNotResendableException("No registered destination remains for this shopper.");
        }

        return replacement;
    }
}
