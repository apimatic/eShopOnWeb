using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    public static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<NotificationResendRecord> _resendRecords;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly ITwilioMessagingClient _messaging;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<OrderNotification> notifications,
        IRepository<NotificationResendRecord> resendRecords,
        IRepository<ContactNumber> contactNumbers,
        ITwilioMessagingClient messaging,
        IAppLogger<OrderNotificationService> logger)
    {
        _notifications = notifications;
        _resendRecords = resendRecords;
        _contactNumbers = contactNumbers;
        _messaging = messaging;
        _logger = logger;
    }

    public Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        return SendToRegisteredDestinationsAsync(
            order,
            OrderNotificationKind.OrderPlaced,
            $"Your eShop order #{order.Id} has been placed. Thank you for shopping with us.",
            scheduleAt: null,
            cancellationToken);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        await SendToRegisteredDestinationsAsync(
            order,
            OrderNotificationKind.OrderDispatched,
            $"Good news — your eShop order #{order.Id} is on its way.",
            scheduleAt: null,
            cancellationToken);

        await SendToRegisteredDestinationsAsync(
            order,
            OrderNotificationKind.DeliveryFollowUp,
            $"How did the delivery of your eShop order #{order.Id} go?",
            scheduleAt: DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay),
            cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        await CancelOutstandingFollowUpsAsync(order.Id, cancellationToken);

        await SendToRegisteredDestinationsAsync(
            order,
            OrderNotificationKind.OrderCancelled,
            $"Your eShop order #{order.Id} has been cancelled.",
            scheduleAt: null,
            cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderIdSpecification(orderId), cancellationToken);
        await RefreshFromProviderAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForBuyerOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken = default)
    {
        var notifications = await ListForOrderAsync(orderId, cancellationToken);
        return notifications.Where(n => n.BuyerId == buyerId).ToList();
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("An idempotency key is required.");
        }

        var existing = await _resendRecords.FirstOrDefaultAsync(
            new NotificationResendByKeySpecification(notificationId, idempotencyKey.Trim()), cancellationToken);
        if (existing != null)
        {
            var previous = await _notifications.GetByIdAsync(existing.ResultNotificationId, cancellationToken);
            if (previous != null)
            {
                await RefreshFromProviderAsync(new[] { previous }, cancellationToken);
                return previous;
            }
        }

        var source = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new KeyNotFoundException("Notification was not found.");

        await RefreshFromProviderAsync(new[] { source }, cancellationToken);

        var destinationStillRegistered = await DestinationStillRegisteredAsync(source, cancellationToken);
        var body = source.ContentRedacted
            ? BodyForKind(source.Kind, source.OrderId)
            : source.Body ?? BodyForKind(source.Kind, source.OrderId);

        var resent = new OrderNotification(source.OrderId, source.BuyerId, source.Kind, source.DestinationNumber, body);
        if (!destinationStillRegistered)
        {
            resent.MarkProviderFailure(null, "Destination is no longer registered.");
            await _notifications.AddAsync(resent, cancellationToken);
        }
        else
        {
            await DeliverAsync(resent, scheduleAt: null, cancellationToken);
        }

        await _resendRecords.AddAsync(new NotificationResendRecord(source.Id, idempotencyKey.Trim(), resent.Id), cancellationToken);
        return resent;
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new KeyNotFoundException("Notification was not found.");

        if (!string.IsNullOrWhiteSpace(notification.ProviderMessageSid))
        {
            var snapshot = await _messaging.RedactBodyAsync(notification.ProviderMessageSid, cancellationToken);
            notification.UpdateProviderState(snapshot.Status, snapshot.ErrorCode, snapshot.ErrorMessage, snapshot.Body);
        }

        notification.MarkContentRedacted();
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    public async Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new ArgumentException("The to timestamp must be on or after from.");
        }

        var providerMessages = await _messaging.ListSentFromConfiguredNumberAsync(from, to, cancellationToken);
        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrWhiteSpace(m.Sid))
            .GroupBy(m => m.Sid)
            .ToDictionary(g => g.Key, g => g.First());

        var applicationInRange = await _notifications.ListAsync(
            new OrderNotificationsCreatedBetweenSpecification(from, to), cancellationToken);

        IReadOnlyList<OrderNotification> matchedBySid = Array.Empty<OrderNotification>();
        if (providerBySid.Count > 0)
        {
            matchedBySid = await _notifications.ListAsync(
                new OrderNotificationsByProviderSidsSpecification(providerBySid.Keys), cancellationToken);
        }

        var applicationBySid = applicationInRange
            .Concat(matchedBySid)
            .Where(n => !string.IsNullOrWhiteSpace(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var seenApplicationIds = new HashSet<int>();
        var entries = new List<NotificationReconciliationEntry>();

        foreach (var provider in providerMessages)
        {
            applicationBySid.TryGetValue(provider.Sid, out var local);
            if (local != null)
            {
                seenApplicationIds.Add(local.Id);
            }

            entries.Add(new NotificationReconciliationEntry(
                local?.Id.ToString(),
                provider.Sid,
                local?.ProviderStatus,
                provider.Status,
                local?.Kind.ToString(),
                provider.DateSent,
                local?.CreatedAt,
                local == null ? "providerOnly" : "matched"));
        }

        foreach (var local in applicationInRange)
        {
            if (seenApplicationIds.Contains(local.Id))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(local.ProviderMessageSid) &&
                providerBySid.ContainsKey(local.ProviderMessageSid))
            {
                continue;
            }

            seenApplicationIds.Add(local.Id);
            entries.Add(new NotificationReconciliationEntry(
                local.Id.ToString(),
                local.ProviderMessageSid,
                local.ProviderStatus,
                null,
                local.Kind.ToString(),
                null,
                local.CreatedAt,
                "applicationOnly"));
        }

        return new NotificationReconciliationReport(from, to, _messaging.ConfiguredFromNumber, entries);
    }

    private async Task SendToRegisteredDestinationsAsync(
        Order order,
        OrderNotificationKind kind,
        string body,
        DateTimeOffset? scheduleAt,
        CancellationToken cancellationToken)
    {
        var destinations = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(order.BuyerId), cancellationToken);
        if (destinations.Count == 0)
        {
            return;
        }

        foreach (var destination in destinations)
        {
            var notification = new OrderNotification(order.Id, order.BuyerId, kind, destination.PhoneNumber, body);
            try
            {
                await DeliverAsync(notification, scheduleAt, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Failed to send {Kind} notification for order {OrderId}: {Message}",
                    kind,
                    order.Id,
                    ex.Message);
                if (notification.Id == 0)
                {
                    notification.MarkProviderFailure(null, "The messaging provider rejected or could not accept the message.");
                    await _notifications.AddAsync(notification, cancellationToken);
                }
            }
        }
    }

    private async Task DeliverAsync(OrderNotification notification, DateTimeOffset? scheduleAt, CancellationToken cancellationToken)
    {
        try
        {
            TwilioMessageSnapshot snapshot;
            if (scheduleAt.HasValue)
            {
                snapshot = await _messaging.ScheduleAsync(notification.DestinationNumber, notification.Body!, scheduleAt.Value, cancellationToken);
            }
            else
            {
                snapshot = await _messaging.SendAsync(notification.DestinationNumber, notification.Body!, cancellationToken);
            }

            notification.MarkAcceptedByProvider(snapshot.Sid, snapshot.Status, scheduleAt);
            notification.UpdateProviderState(snapshot.Status, snapshot.ErrorCode, snapshot.ErrorMessage, snapshot.Body);
        }
        catch (Exception ex)
        {
            notification.MarkProviderFailure(null, "The messaging provider rejected or could not accept the message.");
            _logger.LogWarning("Messaging provider call failed for order {OrderId} kind {Kind}: {Message}", notification.OrderId, notification.Kind, ex.Message);
            await _notifications.AddAsync(notification, cancellationToken);
            return;
        }

        await _notifications.AddAsync(notification, cancellationToken);
    }

    private async Task CancelOutstandingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderIdSpecification(orderId), cancellationToken);
        foreach (var followUp in notifications.Where(n => n.Kind == OrderNotificationKind.DeliveryFollowUp))
        {
            if (string.IsNullOrWhiteSpace(followUp.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var current = await _messaging.FetchAsync(followUp.ProviderMessageSid, cancellationToken);
                followUp.UpdateProviderState(current.Status, current.ErrorCode, current.ErrorMessage, current.Body);

                if (string.Equals(current.Status, "scheduled", StringComparison.OrdinalIgnoreCase))
                {
                    var cancelled = await _messaging.CancelScheduledAsync(followUp.ProviderMessageSid, cancellationToken);
                    followUp.UpdateProviderState(cancelled.Status, cancelled.ErrorCode, cancelled.ErrorMessage, cancelled.Body);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not cancel scheduled follow-up {NotificationId} for order {OrderId}: {Message}", followUp.Id, orderId, ex.Message);
            }

            await _notifications.UpdateAsync(followUp, cancellationToken);
        }
    }

    private async Task RefreshFromProviderAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrWhiteSpace(notification.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var snapshot = await _messaging.FetchAsync(notification.ProviderMessageSid, cancellationToken);
                notification.UpdateProviderState(snapshot.Status, snapshot.ErrorCode, snapshot.ErrorMessage, snapshot.Body);
                if (notification.ContentRedacted || snapshot.Body == string.Empty)
                {
                    notification.MarkContentRedacted();
                }

                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not refresh provider status for notification {NotificationId}: {Message}", notification.Id, ex.Message);
            }
        }
    }

    private async Task<bool> DestinationStillRegisteredAsync(OrderNotification source, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(source.BuyerId), cancellationToken);
        return numbers.Any(n => n.PhoneNumber == source.DestinationNumber);
    }

    private static string BodyForKind(OrderNotificationKind kind, int orderId)
    {
        return kind switch
        {
            OrderNotificationKind.OrderPlaced => $"Your eShop order #{orderId} has been placed. Thank you for shopping with us.",
            OrderNotificationKind.OrderDispatched => $"Good news — your eShop order #{orderId} is on its way.",
            OrderNotificationKind.DeliveryFollowUp => $"How did the delivery of your eShop order #{orderId} go?",
            OrderNotificationKind.OrderCancelled => $"Your eShop order #{orderId} has been cancelled.",
            _ => $"Update for your eShop order #{orderId}."
        };
    }
}
