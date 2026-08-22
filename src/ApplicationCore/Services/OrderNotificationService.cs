using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    public static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly ITwilioMessagingClient _messaging;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<OrderNotification> notifications,
        IRepository<ContactNumber> contactNumbers,
        ITwilioMessagingClient messaging,
        IAppLogger<OrderNotificationService> logger)
    {
        _notifications = notifications;
        _contactNumbers = contactNumbers;
        _messaging = messaging;
        _logger = logger;
    }

    public Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        return TrySendAsync(
            order,
            OrderNotificationKind.OrderPlaced,
            $"Your eShop order #{order.Id} has been placed. Thank you for shopping with us.",
            sendAt: null,
            cancellationToken);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        await TrySendAsync(
            order,
            OrderNotificationKind.OrderDispatched,
            $"Your eShop order #{order.Id} is on its way.",
            sendAt: null,
            cancellationToken);

        await TrySendAsync(
            order,
            OrderNotificationKind.DeliveryFollowUp,
            $"How did the delivery of your eShop order #{order.Id} go?",
            DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay),
            cancellationToken);
    }

    public Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        return TrySendAsync(
            order,
            OrderNotificationKind.OrderCancelled,
            $"Your eShop order #{order.Id} has been cancelled.",
            sendAt: null,
            cancellationToken);
    }

    public async Task CancelPendingFollowUpsAsync(Order order, CancellationToken cancellationToken = default)
    {
        var followUps = await _notifications.ListAsync(new ScheduledFollowUpsByOrderIdSpec(order.Id), cancellationToken);
        foreach (var followUp in followUps)
        {
            await CancelWithProviderAsync(followUp, cancellationToken);
        }
    }

    public async Task CancelScheduledForDestinationAsync(string destinationPhoneNumber, CancellationToken cancellationToken = default)
    {
        var scheduled = await _notifications.ListAsync(
            new ScheduledNotificationsByDestinationSpec(destinationPhoneNumber), cancellationToken);
        foreach (var notification in scheduled)
        {
            await CancelWithProviderAsync(notification, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var items = await _notifications.ListAsync(new NotificationsByOrderIdSpec(orderId), cancellationToken);
        await RefreshFromProviderAsync(items, cancellationToken);
        return items;
    }

    public Task<IReadOnlyList<OrderNotification>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return ListAndRefresh(new NotificationsByBuyerIdSpec(buyerId), cancellationToken);
    }

    public async Task RefreshFromProviderAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken = default)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var record = await _messaging.FetchAsync(notification.ProviderMessageSid, cancellationToken);
                if (record == null)
                {
                    continue;
                }

                ApplyProviderRecord(notification, record);
                if (record.Body != null && record.Body.Length == 0)
                {
                    notification.RedactContent();
                }
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception)
            {
                _logger.LogWarning("Failed to refresh provider status for notification {NotificationId}.", notification.Id);
            }
        }
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new OrderLifecycleException("An idempotency key is required.");
        }

        var existing = await _notifications.FirstOrDefaultAsync(
            new NotificationByResendKeySpec(notificationId, idempotencyKey.Trim()), cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        var source = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (source == null)
        {
            throw new KeyNotFoundException("Notification not found.");
        }

        if (source.ContentRedacted || string.IsNullOrEmpty(source.Body))
        {
            throw new OrderLifecycleException("The original message content is no longer available to resend.");
        }

        if (!source.DidNotReachShopper())
        {
            throw new OrderLifecycleException("Only messages that did not reach the shopper can be resent.");
        }

        var destinationStillRegistered = await DestinationStillRegisteredAsync(source, cancellationToken);
        if (!destinationStillRegistered)
        {
            throw new OrderLifecycleException("The destination number is no longer on file and cannot be messaged.");
        }

        var resend = new OrderNotification(
            source.OrderId,
            source.BuyerId,
            OrderNotificationKind.Resend,
            source.Body,
            source.DestinationPhoneNumber,
            source.ContactNumberId);
        resend.ApplyAsResend(source.Id, idempotencyKey.Trim());
        await _notifications.AddAsync(resend, cancellationToken);

        await DispatchToProviderAsync(resend, sendAt: null, cancellationToken);
        return resend;
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (notification == null)
        {
            throw new KeyNotFoundException("Notification not found.");
        }

        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            var record = await _messaging.RedactBodyAsync(notification.ProviderMessageSid, cancellationToken);
            ApplyProviderRecord(notification, record);
            if (!string.IsNullOrEmpty(record.Body))
            {
                throw new OrderLifecycleException("The provider did not dispose of the message content.");
            }
        }

        notification.RedactContent();
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    public async Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new OrderLifecycleException("The 'to' timestamp must be on or after 'from'.");
        }

        var fromNumber = _messaging.FromNumber;

        var providerMessages = await _messaging.ListFromNumberAsync(fromNumber, from, to, cancellationToken);
        var local = await _notifications.ListAsync(new NotificationsInRangeSpec(from, to), cancellationToken);

        var localBySid = local
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();
        var seenSids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var message in providerMessages)
        {
            if (string.IsNullOrEmpty(message.Sid))
            {
                continue;
            }

            seenSids.Add(message.Sid);
            if (localBySid.TryGetValue(message.Sid, out var notification))
            {
                matched.Add(new ReconciliationEntry
                {
                    ProviderMessageSid = message.Sid,
                    NotificationId = notification.Id,
                    ProviderStatus = message.Status,
                    LocalStatus = notification.ProviderStatus,
                    Match = "matched"
                });
            }
            else
            {
                providerOnly.Add(new ReconciliationEntry
                {
                    ProviderMessageSid = message.Sid,
                    ProviderStatus = message.Status,
                    Match = "providerOnly"
                });
            }
        }

        var localOnly = local
            .Where(n => string.IsNullOrEmpty(n.ProviderMessageSid) || !seenSids.Contains(n.ProviderMessageSid))
            .Select(n => new ReconciliationEntry
            {
                ProviderMessageSid = n.ProviderMessageSid,
                NotificationId = n.Id,
                LocalStatus = n.ProviderStatus,
                Match = "localOnly"
            })
            .ToList();

        return new NotificationReconciliationReport
        {
            From = from,
            To = to,
            FromNumber = fromNumber,
            Matched = matched,
            ProviderOnly = providerOnly,
            LocalOnly = localOnly
        };
    }

    private async Task<IReadOnlyList<OrderNotification>> ListAndRefresh(
        NotificationsByBuyerIdSpec spec,
        CancellationToken cancellationToken)
    {
        var items = await _notifications.ListAsync(spec, cancellationToken);
        await RefreshFromProviderAsync(items, cancellationToken);
        return items;
    }

    private async Task TrySendAsync(
        Order order,
        OrderNotificationKind kind,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var destination = await ResolveDestinationAsync(order.BuyerId, cancellationToken);
            if (destination == null)
            {
                _logger.LogInformation("Skipping {Kind} notification for order {OrderId}; no contact number on file.", kind, order.Id);
                return;
            }

            var notification = new OrderNotification(order.Id, order.BuyerId, kind, body, destination.CanonicalPhoneNumber, destination.Id);
            if (sendAt.HasValue)
            {
                notification.MarkScheduled(sendAt.Value);
            }

            await _notifications.AddAsync(notification, cancellationToken);
            await DispatchToProviderAsync(notification, sendAt, cancellationToken);
        }
        catch (Exception)
        {
            _logger.LogWarning("Notification {Kind} for order {OrderId} could not be sent; the order operation still succeeded.", kind, order.Id);
        }
    }

    private async Task DispatchToProviderAsync(OrderNotification notification, DateTimeOffset? sendAt, CancellationToken cancellationToken)
    {
        try
        {
            var record = await _messaging.SendAsync(new SendMessageRequest
            {
                To = notification.DestinationPhoneNumber,
                Body = notification.Body ?? string.Empty,
                SendAt = sendAt
            }, cancellationToken);

            ApplyProviderRecord(notification, record);
            await _notifications.UpdateAsync(notification, cancellationToken);
            _logger.LogInformation(
                "Provider accepted notification {NotificationId} for order {OrderId} as {Sid} with status {Status}.",
                notification.Id, notification.OrderId, notification.ProviderMessageSid ?? "(none)", notification.ProviderStatus);
        }
        catch (Exception)
        {
            notification.MarkSendFailed("The messaging provider rejected or failed the send.");
            await _notifications.UpdateAsync(notification, cancellationToken);
            _logger.LogWarning("Provider send failed for notification {NotificationId} on order {OrderId}.", notification.Id, notification.OrderId);
        }
    }

    private async Task CancelWithProviderAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            return;
        }

        try
        {
            var record = await _messaging.CancelScheduledAsync(notification.ProviderMessageSid, cancellationToken);
            ApplyProviderRecord(notification, record);
            await _notifications.UpdateAsync(notification, cancellationToken);
            _logger.LogInformation("Cancelled scheduled provider message for notification {NotificationId}.", notification.Id);
        }
        catch (Exception)
        {
            _logger.LogWarning("Failed to cancel scheduled provider message for notification {NotificationId}.", notification.Id);
        }
    }

    private async Task<ContactNumber?> ResolveDestinationAsync(string buyerId, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerIdSpec(buyerId), cancellationToken);
        return numbers.FirstOrDefault();
    }

    private async Task<bool> DestinationStillRegisteredAsync(OrderNotification source, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerIdSpec(source.BuyerId), cancellationToken);
        return numbers.Any(n => n.CanonicalPhoneNumber == source.DestinationPhoneNumber);
    }

    private static void ApplyProviderRecord(OrderNotification notification, TwilioMessageRecord record)
    {
        notification.AttachProviderResult(
            record.Sid,
            record.Status ?? notification.ProviderStatus,
            record.ErrorCode,
            SanitizeProviderError(record.ErrorMessage, notification.DestinationPhoneNumber));
    }

    private static string? SanitizeProviderError(string? errorMessage, string destination)
    {
        if (string.IsNullOrEmpty(errorMessage))
        {
            return errorMessage;
        }

        return errorMessage.Replace(destination, "[redacted]", StringComparison.OrdinalIgnoreCase);
    }
}
