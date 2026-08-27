using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
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
    private readonly IContactNumberService _contactNumbers;
    private readonly ITwilioMessagingClient _messaging;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<OrderNotification> notifications,
        IRepository<NotificationResendRecord> resendRecords,
        IContactNumberService contactNumbers,
        ITwilioMessagingClient messaging,
        IAppLogger<OrderNotificationService> logger)
    {
        _notifications = notifications;
        _resendRecords = resendRecords;
        _contactNumbers = contactNumbers;
        _messaging = messaging;
        _logger = logger;
    }

    public Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default) =>
        TrySendAsync(order, OrderNotificationKind.OrderPlaced, sendAt: null, relatedNotificationId: null, cancellationToken);

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var dispatch = await TrySendAsync(order, OrderNotificationKind.OrderDispatched, sendAt: null, relatedNotificationId: null, cancellationToken);
        await TrySendAsync(
            order,
            OrderNotificationKind.DeliveryFollowUp,
            DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay),
            dispatch?.Id,
            cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        await CancelPendingFollowUpsAsync(order.Id, cancellationToken);
        await TrySendAsync(order, OrderNotificationKind.OrderCancelled, sendAt: null, relatedNotificationId: null, cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(
        int orderId,
        bool refreshFromProvider,
        CancellationToken cancellationToken = default)
    {
        var items = await _notifications.ListAsync(new OrderNotificationsByOrderIdSpecification(orderId), cancellationToken);
        if (refreshFromProvider)
        {
            await RefreshAsync(items, cancellationToken);
        }

        return items;
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForOrdersAsync(
        IReadOnlyCollection<int> orderIds,
        bool refreshFromProvider,
        CancellationToken cancellationToken = default)
    {
        if (orderIds.Count == 0)
        {
            return Array.Empty<OrderNotification>();
        }

        var items = await _notifications.ListAsync(new OrderNotificationsByOrderIdsSpecification(orderIds), cancellationToken);
        if (refreshFromProvider)
        {
            await RefreshAsync(items, cancellationToken);
        }

        return items;
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NegativeOrZero(notificationId, nameof(notificationId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var existing = await _resendRecords.FirstOrDefaultAsync(
            new NotificationResendByKeySpecification(notificationId, idempotencyKey),
            cancellationToken);
        if (existing is not null)
        {
            var previous = await _notifications.GetByIdAsync(existing.ResultNotificationId, cancellationToken);
            if (previous is not null)
            {
                return previous;
            }
        }

        var source = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new KeyNotFoundException($"Notification {notificationId} was not found.");

        var body = source.ContentRedacted || string.IsNullOrEmpty(source.Body)
            ? OrderSmsTemplates.For(source.Kind, source.OrderId)
            : source.Body;

        var contact = await _contactNumbers.GetPreferredAsync(source.BuyerId, cancellationToken);
        var resend = new OrderNotification(
            source.OrderId,
            source.BuyerId,
            source.Kind,
            body,
            scheduledAt: null,
            relatedNotificationId: source.Id);

        if (contact is null)
        {
            resend.MarkSkippedNoDestination();
            await _notifications.AddAsync(resend, cancellationToken);
            await StoreResendKeyAsync(notificationId, idempotencyKey, resend.Id, cancellationToken);
            return resend;
        }

        try
        {
            var message = await _messaging.SendAsync(contact.PhoneNumber, body, cancellationToken);
            Apply(resend, message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Resend failed for notification {NotificationId}: {Message}", notificationId, SafeMessage(ex));
            resend.MarkFailed(PhoneNumberSanitizer.Sanitize(ex.Message));
        }

        await _notifications.AddAsync(resend, cancellationToken);
        await StoreResendKeyAsync(notificationId, idempotencyKey, resend.Id, cancellationToken);
        return resend;
    }

    public async Task<OrderNotification> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new KeyNotFoundException($"Notification {notificationId} was not found.");

        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            var provider = await _messaging.RedactBodyAsync(notification.ProviderMessageSid, cancellationToken);
            Apply(notification, provider);
        }

        notification.RedactLocalContent();
        await _notifications.UpdateAsync(notification, cancellationToken);
        return notification;
    }

    public async Task<NotificationReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new ArgumentException("The 'to' timestamp must be on or after 'from'.");
        }

        var providerMessages = await _messaging.ListSentFromConfiguredNumberAsync(from, to, cancellationToken);
        var local = await _notifications.ListAsync(new OrderNotificationsByCreatedRangeSpecification(from, to), cancellationToken);

        var localBySid = local
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var items = new List<NotificationReconciliationItem>();
        var matchedSids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var provider in providerMessages)
        {
            if (string.IsNullOrEmpty(provider.Sid))
            {
                continue;
            }

            if (localBySid.TryGetValue(provider.Sid, out var localMatch))
            {
                matchedSids.Add(provider.Sid);
                items.Add(new NotificationReconciliationItem(
                    "matched",
                    localMatch.Id,
                    provider.Sid,
                    provider.Status,
                    localMatch.ProviderStatus,
                    provider.DateSent ?? localMatch.ProviderDateSent));
            }
            else
            {
                items.Add(new NotificationReconciliationItem(
                    "providerOnly",
                    null,
                    provider.Sid,
                    provider.Status,
                    null,
                    provider.DateSent));
            }
        }

        foreach (var notification in local)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid) || !matchedSids.Contains(notification.ProviderMessageSid))
            {
                items.Add(new NotificationReconciliationItem(
                    string.IsNullOrEmpty(notification.ProviderMessageSid) ? "eShopOnly" : "eShopOnly",
                    notification.Id,
                    notification.ProviderMessageSid,
                    null,
                    notification.ProviderStatus,
                    notification.ProviderDateSent));
            }
        }

        var eShopWithSid = local.Count(n => !string.IsNullOrEmpty(n.ProviderMessageSid));
        return new NotificationReconciliationReport(
            from,
            to,
            providerMessages.Count,
            eShopWithSid,
            items);
    }

    private async Task StoreResendKeyAsync(int sourceId, string key, int resultId, CancellationToken cancellationToken)
    {
        var record = new NotificationResendRecord(sourceId, key, resultId);
        await _resendRecords.AddAsync(record, cancellationToken);
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var followUps = await _notifications.ListAsync(new CancellableFollowUpsByOrderSpecification(orderId), cancellationToken);
        foreach (var followUp in followUps)
        {
            if (!followUp.CanBeCancelledWithProvider() || string.IsNullOrEmpty(followUp.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var result = await _messaging.CancelAsync(followUp.ProviderMessageSid, cancellationToken);
                Apply(followUp, result);
                await _notifications.UpdateAsync(followUp, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Could not cancel follow-up notification {NotificationId} for order {OrderId}: {Message}",
                    followUp.Id,
                    orderId,
                    SafeMessage(ex));
            }
        }
    }

    private async Task<OrderNotification?> TrySendAsync(
        Order order,
        OrderNotificationKind kind,
        DateTimeOffset? sendAt,
        int? relatedNotificationId,
        CancellationToken cancellationToken)
    {
        var body = OrderSmsTemplates.For(kind, order.Id);
        var notification = new OrderNotification(order.Id, order.BuyerId, kind, body, sendAt, relatedNotificationId);

        try
        {
            var contact = await _contactNumbers.GetPreferredAsync(order.BuyerId, cancellationToken);
            if (contact is null)
            {
                notification.MarkSkippedNoDestination();
                await _notifications.AddAsync(notification, cancellationToken);
                return notification;
            }

            ProviderMessage message;
            if (sendAt.HasValue)
            {
                message = await _messaging.ScheduleAsync(contact.PhoneNumber, body, sendAt.Value, cancellationToken);
            }
            else
            {
                message = await _messaging.SendAsync(contact.PhoneNumber, body, cancellationToken);
            }

            Apply(notification, message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "SMS notification {Kind} for order {OrderId} did not send: {Message}",
                kind,
                order.Id,
                SafeMessage(ex));
            notification.MarkFailed(PhoneNumberSanitizer.Sanitize(ex.Message));
        }

        await _notifications.AddAsync(notification, cancellationToken);
        return notification;
    }

    private async Task RefreshAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var provider = await _messaging.FetchAsync(notification.ProviderMessageSid, cancellationToken);
                Apply(notification, provider);
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Could not refresh provider status for notification {NotificationId}: {Message}",
                    notification.Id,
                    SafeMessage(ex));
            }
        }
    }

    private static string SafeMessage(Exception ex) =>
        PhoneNumberSanitizer.Sanitize(ex.Message) ?? "An error occurred.";

    private static void Apply(OrderNotification notification, ProviderMessage message)
    {
        notification.ApplyProviderResult(
            message.Sid,
            message.Status,
            message.ErrorCode,
            PhoneNumberSanitizer.Sanitize(message.ErrorMessage),
            message.DateSent,
            message.Body);
    }
}

internal static class OrderSmsTemplates
{
    public static string For(OrderNotificationKind kind, int orderId) => kind switch
    {
        OrderNotificationKind.OrderPlaced =>
            $"Your eShop order #{orderId} has been placed. We will notify you when it ships.",
        OrderNotificationKind.OrderDispatched =>
            $"Your eShop order #{orderId} is on its way.",
        OrderNotificationKind.DeliveryFollowUp =>
            $"How did the delivery of your eShop order #{orderId} go?",
        OrderNotificationKind.OrderCancelled =>
            $"Your eShop order #{orderId} has been cancelled.",
        _ => $"Update for your eShop order #{orderId}."
    };
}
