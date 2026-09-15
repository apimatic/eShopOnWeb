using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class NotificationOperatorService : INotificationOperatorService
{
    private readonly IRepository<OrderNotification> _notifications;
    private readonly ITwilioMessagingClient _messaging;
    private readonly OrderSmsNotifier _notifier;
    private readonly IAppLogger<NotificationOperatorService> _logger;

    public NotificationOperatorService(
        IRepository<OrderNotification> notifications,
        ITwilioMessagingClient messaging,
        OrderSmsNotifier notifier,
        IAppLogger<NotificationOperatorService> logger)
    {
        _notifications = notifications;
        _messaging = messaging;
        _notifier = notifier;
        _logger = logger;
    }

    public async Task<ResendNotificationResult?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new CatalogOrderException("An idempotency key is required.");
        }

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (original == null)
        {
            return null;
        }

        var existing = await _notifications.FirstOrDefaultAsync(
            new OrderNotificationByIdempotencySpecification(notificationId, idempotencyKey),
            cancellationToken);
        if (existing != null)
        {
            return new ResendNotificationResult(existing.Id, true);
        }

        if (!await _notifier.IsDestinationActiveAsync(original.BuyerId, original.DestinationNumber, cancellationToken))
        {
            throw new CatalogOrderException("The original destination is no longer on file and cannot be messaged.");
        }

        if (original.ContentRedacted || string.IsNullOrEmpty(original.Body))
        {
            throw new CatalogOrderException("The message content is no longer available to resend.");
        }

        var resend = await _notifier.TryResendAsync(original, original.DestinationNumber, idempotencyKey, cancellationToken);
        if (resend == null)
        {
            throw new CatalogOrderException("The message could not be resent.");
        }

        return new ResendNotificationResult(resend.Id, false);
    }

    public async Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (notification == null)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            try
            {
                await _messaging.RedactBodyAsync(notification.ProviderMessageSid, cancellationToken);
            }
            catch (Exception)
            {
                _logger.LogWarning("Failed to redact provider content for notification {NotificationId}.", notification.Id);
                throw;
            }
        }

        notification.RedactContent();
        await _notifications.UpdateAsync(notification, cancellationToken);
        return true;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var providerMessages = await _messaging.ListFromConfiguredNumberAsync(from, to, cancellationToken);
        var providerSids = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid))
            .Select(m => m.Sid)
            .ToList();

        var matchedBySid = providerSids.Count == 0
            ? new List<OrderNotification>()
            : await _notifications.ListAsync(new OrderNotificationsByProviderSidsSpecification(providerSids), cancellationToken);

        var localInRange = await _notifications.ListAsync(
            new OrderNotificationsInCreatedRangeSpecification(from, to),
            cancellationToken);

        var localBySid = matchedBySid
            .Concat(localInRange)
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new List<ReconciliationMessage>();
        var providerOnly = new List<ReconciliationMessage>();
        var seenSids = new HashSet<string>();

        foreach (var provider in providerMessages)
        {
            seenSids.Add(provider.Sid);
            if (localBySid.TryGetValue(provider.Sid, out var local))
            {
                matched.Add(new ReconciliationMessage(
                    provider.Sid,
                    local.Id,
                    provider.Status,
                    local.ProviderStatus,
                    provider.DateSent ?? local.ProviderDateSent));
            }
            else
            {
                providerOnly.Add(new ReconciliationMessage(
                    provider.Sid,
                    null,
                    provider.Status,
                    null,
                    provider.DateSent));
            }
        }

        var applicationOnly = localInRange
            .Where(n => string.IsNullOrEmpty(n.ProviderMessageSid) || !seenSids.Contains(n.ProviderMessageSid))
            .Select(n => new ReconciliationMessage(
                n.ProviderMessageSid,
                n.Id,
                null,
                n.ProviderStatus,
                n.ProviderDateSent ?? n.CreatedAt))
            .ToList();

        return new ReconciliationReport(from, to, matched, providerOnly, applicationOnly);
    }
}
