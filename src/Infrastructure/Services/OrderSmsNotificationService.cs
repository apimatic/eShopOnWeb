using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class OrderSmsNotificationService : IOrderSmsNotificationService
{
    public static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<ShopperContactNumber> _contactNumbers;
    private readonly ITwilioMessagingClient _messaging;
    private readonly TwilioSettings _settings;
    private readonly ILogger<OrderSmsNotificationService> _logger;

    public OrderSmsNotificationService(
        IRepository<OrderNotification> notifications,
        IRepository<ShopperContactNumber> contactNumbers,
        ITwilioMessagingClient messaging,
        IOptions<TwilioSettings> settings,
        ILogger<OrderSmsNotificationService> logger)
    {
        _notifications = notifications;
        _contactNumbers = contactNumbers;
        _messaging = messaging;
        _settings = settings.Value;
        _logger = logger;
    }

    public Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default) =>
        NotifyAsync(
            order,
            OrderNotificationKind.OrderPlaced,
            $"eShopOnWeb: Your order #{order.Id} has been placed. Thank you for shopping with us.",
            sendAt: null,
            cancellationToken);

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        await NotifyAsync(
            order,
            OrderNotificationKind.OrderDispatched,
            $"eShopOnWeb: Your order #{order.Id} is on its way.",
            sendAt: null,
            cancellationToken);

        var sendAt = DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay);
        await NotifyAsync(
            order,
            OrderNotificationKind.DeliveryFollowUp,
            $"eShopOnWeb: How did the delivery of order #{order.Id} go? We'd love your feedback.",
            sendAt,
            cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        await CancelPendingFollowUpsAsync(order.Id, cancellationToken);

        await NotifyAsync(
            order,
            OrderNotificationKind.OrderCancelled,
            $"eShopOnWeb: Your order #{order.Id} has been cancelled.",
            sendAt: null,
            cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var items = await _notifications.ListAsync(new OrderNotificationsByOrderIdSpecification(orderId), cancellationToken);
        await RefreshFromProviderAsync(items, cancellationToken);
        return items;
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForBuyerOrdersAsync(IReadOnlyCollection<int> orderIds, CancellationToken cancellationToken = default)
    {
        if (orderIds.Count == 0)
        {
            return Array.Empty<OrderNotification>();
        }

        var items = await _notifications.ListAsync(new OrderNotificationsByOrderIdsSpecification(orderIds), cancellationToken);
        await RefreshFromProviderAsync(items, cancellationToken);
        return items;
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("An idempotency key is required.", nameof(idempotencyKey));
        }

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken)
                       ?? throw new KeyNotFoundException("Notification was not found.");

        var existing = await _notifications.FirstOrDefaultAsync(
            new OrderNotificationByResendIdempotencySpecification(original.Id, idempotencyKey),
            cancellationToken);
        if (existing is not null)
        {
            await RefreshFromProviderAsync(new[] { existing }, cancellationToken);
            return existing;
        }

        if (original.ContentRedacted || string.IsNullOrEmpty(original.Body))
        {
            throw new InvalidOperationException("The original message content is no longer available to resend.");
        }

        var destinations = await ActiveDestinationsAsync(original.BuyerId, cancellationToken);
        var destination = destinations.FirstOrDefault(d => d.CanonicalNumber == original.DestinationNumber);
        if (destination is null)
        {
            throw new InvalidOperationException("The destination number is no longer on file for this shopper.");
        }

        var resent = new OrderNotification(
            original.OrderId,
            original.BuyerId,
            destination.Id,
            destination.CanonicalNumber,
            OrderNotificationKind.Resend,
            original.Body,
            providerMessageSid: null,
            providerStatus: "pending",
            providerErrorCode: null,
            providerErrorMessage: null,
            scheduledFor: null,
            resentFromNotificationId: original.Id,
            idempotencyKey: idempotencyKey);

        resent = await _notifications.AddAsync(resent, cancellationToken);
        await DeliverAsync(resent, sendAt: null, cancellationToken);
        return resent;
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken)
                           ?? throw new KeyNotFoundException("Notification was not found.");

        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            Exception? lastError = null;
            for (var attempt = 1; attempt <= 5; attempt++)
            {
                try
                {
                    await _messaging.RedactMessageBodyAsync(notification.ProviderMessageSid, cancellationToken);
                    lastError = null;
                    break;
                }
                catch (Exception ex) when (attempt < 5)
                {
                    lastError = ex;
                    await Task.Delay(TimeSpan.FromMilliseconds(300 * attempt), cancellationToken);
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }
            }

            if (lastError is not null)
            {
                _logger.LogWarning(
                    "Failed to redact provider content for notification {NotificationId} (SID present).",
                    notification.Id);
                throw lastError;
            }
        }

        notification.MarkContentRedacted();
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    public async Task<NotificationReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var fromNumber = _settings.FromNumber;
        var providerMessages = await _messaging.ListMessagesFromAsync(fromNumber, from, to, cancellationToken);
        var providerBySid = providerMessages
            .GroupBy(m => m.Sid)
            .ToDictionary(g => g.Key, g => g.First());

        var localInRange = await _notifications.ListAsync(
            new OrderNotificationsInCreatedRangeSpecification(from, to),
            cancellationToken);

        IReadOnlyList<OrderNotification> localBySid = Array.Empty<OrderNotification>();
        if (providerBySid.Count > 0)
        {
            localBySid = await _notifications.ListAsync(
                new OrderNotificationsByProviderSidsSpecification(providerBySid.Keys.ToArray()),
                cancellationToken);
        }

        var localByProviderSid = localInRange
            .Concat(localBySid)
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new List<NotificationReconciliationEntry>();
        var providerOnly = new List<NotificationReconciliationEntry>();
        var applicationOnly = new List<NotificationReconciliationEntry>();

        foreach (var provider in providerBySid.Values)
        {
            if (localByProviderSid.TryGetValue(provider.Sid, out var local))
            {
                matched.Add(new NotificationReconciliationEntry(
                    provider.Sid,
                    local.Id,
                    provider.Status,
                    local.ProviderStatus,
                    "matched"));
            }
            else
            {
                providerOnly.Add(new NotificationReconciliationEntry(
                    provider.Sid,
                    null,
                    provider.Status,
                    null,
                    "provider"));
            }
        }

        var seenSids = new HashSet<string>(providerBySid.Keys);
        foreach (var local in localInRange)
        {
            if (!string.IsNullOrEmpty(local.ProviderMessageSid) && seenSids.Contains(local.ProviderMessageSid))
            {
                continue;
            }

            applicationOnly.Add(new NotificationReconciliationEntry(
                local.ProviderMessageSid,
                local.Id,
                null,
                local.ProviderStatus,
                "application"));
        }

        return new NotificationReconciliationReport(from, to, fromNumber, matched, providerOnly, applicationOnly);
    }

    private async Task NotifyAsync(
        Order order,
        OrderNotificationKind kind,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var destinations = await ActiveDestinationsAsync(order.BuyerId, cancellationToken);
            if (destinations.Count == 0)
            {
                return;
            }

            foreach (var destination in destinations)
            {
                var notification = new OrderNotification(
                    order.Id,
                    order.BuyerId,
                    destination.Id,
                    destination.CanonicalNumber,
                    kind,
                    body,
                    providerMessageSid: null,
                    providerStatus: "pending",
                    providerErrorCode: null,
                    providerErrorMessage: null,
                    scheduledFor: sendAt,
                    resentFromNotificationId: null,
                    idempotencyKey: null);

                notification = await _notifications.AddAsync(notification, cancellationToken);
                await DeliverAsync(notification, sendAt, cancellationToken);
            }
        }
        catch (Exception)
        {
            _logger.LogWarning("Order {OrderId} notification of kind {Kind} failed; the order operation still succeeded.", order.Id, kind);
        }
    }

    private async Task DeliverAsync(OrderNotification notification, DateTimeOffset? sendAt, CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await _messaging.CreateMessageAsync(
                new CreateTwilioMessageRequest(notification.DestinationNumber, notification.Body ?? string.Empty, sendAt),
                cancellationToken);
            notification.AttachProviderMessage(snapshot.Sid, snapshot.Status, snapshot.ErrorCode, snapshot.ErrorMessage);
            await _notifications.UpdateAsync(notification, cancellationToken);
        }
        catch (Exception)
        {
            _logger.LogWarning("Provider send failed for notification {NotificationId}.", notification.Id);
            notification.MarkSendFailed("failed", "Provider send failed.");
            await _notifications.UpdateAsync(notification, cancellationToken);
        }
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        try
        {
            var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderIdSpecification(orderId), cancellationToken);
            foreach (var followUp in notifications.Where(n => n.IsPendingFollowUp()))
            {
                try
                {
                    var snapshot = await _messaging.CancelScheduledMessageAsync(followUp.ProviderMessageSid!, cancellationToken);
                    followUp.ApplyProviderState(snapshot.Status, snapshot.ErrorCode, snapshot.ErrorMessage, snapshot.Body);
                    await _notifications.UpdateAsync(followUp, cancellationToken);
                }
                catch (Exception)
                {
                    _logger.LogWarning("Failed to cancel scheduled follow-up notification {NotificationId}.", followUp.Id);
                }
            }
        }
        catch (Exception)
        {
            _logger.LogWarning("Failed to cancel scheduled follow-ups for order {OrderId}.", orderId);
        }
    }

    private async Task RefreshFromProviderAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var snapshot = await _messaging.FetchMessageAsync(notification.ProviderMessageSid, cancellationToken);
                if (!notification.ContentRedacted
                    && string.IsNullOrEmpty(snapshot.Body)
                    && !string.IsNullOrEmpty(notification.Body))
                {
                    notification.MarkContentRedacted();
                }

                notification.ApplyProviderState(
                    snapshot.Status,
                    snapshot.ErrorCode,
                    snapshot.ErrorMessage,
                    notification.ContentRedacted ? null : snapshot.Body);

                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (HttpRequestException)
            {
                _logger.LogWarning("Could not refresh provider status for notification {NotificationId}.", notification.Id);
            }
            catch (Exception)
            {
                _logger.LogWarning("Could not refresh provider status for notification {NotificationId}.", notification.Id);
            }
        }
    }

    private async Task<IReadOnlyList<ShopperContactNumber>> ActiveDestinationsAsync(string buyerId, CancellationToken cancellationToken)
    {
        return await _contactNumbers.ListAsync(new ShopperContactNumbersSpecification(buyerId), cancellationToken);
    }
}
