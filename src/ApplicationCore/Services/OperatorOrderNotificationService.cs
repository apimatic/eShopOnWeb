using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OperatorOrderNotificationService : IOperatorOrderNotificationService
{
    private readonly IRepository<Order> _orders;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly ISmsGateway _smsGateway;
    private readonly OrderNotificationDispatcher _dispatcher;
    private readonly IAppLogger<OperatorOrderNotificationService> _logger;

    public OperatorOrderNotificationService(
        IRepository<Order> orders,
        IRepository<OrderNotification> notifications,
        ISmsGateway smsGateway,
        OrderNotificationDispatcher dispatcher,
        IAppLogger<OperatorOrderNotificationService> logger)
    {
        _orders = orders;
        _notifications = notifications;
        _smsGateway = smsGateway;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    public async Task<Order> DispatchAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken)
                    ?? throw new KeyNotFoundException($"Order {orderId} was not found.");

        try
        {
            order.MarkDispatched();
        }
        catch (InvalidOperationException ex)
        {
            throw new OrderStateException(ex.Message);
        }

        await _orders.UpdateAsync(order, cancellationToken);

        await _dispatcher.NotifyAsync(
            order.Id,
            order.BuyerId,
            NotificationKind.OrderDispatched,
            OrderNotificationDispatcher.DispatchedBody(order.Id),
            sendAt: null,
            parentNotificationId: null,
            idempotencyKey: null,
            destinationOverride: null,
            cancellationToken);

        await _dispatcher.NotifyAsync(
            order.Id,
            order.BuyerId,
            NotificationKind.DeliveryFollowUp,
            OrderNotificationDispatcher.FollowUpBody(order.Id),
            sendAt: DateTimeOffset.UtcNow.Add(OrderNotificationDispatcher.FollowUpDelay),
            parentNotificationId: null,
            idempotencyKey: null,
            destinationOverride: null,
            cancellationToken);

        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken)
                    ?? throw new KeyNotFoundException($"Order {orderId} was not found.");

        try
        {
            order.MarkCancelled();
        }
        catch (InvalidOperationException ex)
        {
            throw new OrderStateException(ex.Message);
        }

        await _orders.UpdateAsync(order, cancellationToken);

        await _dispatcher.CancelFollowUpsAsync(order.Id, cancellationToken);

        await _dispatcher.NotifyAsync(
            order.Id,
            order.BuyerId,
            NotificationKind.OrderCancelled,
            OrderNotificationDispatcher.CancelledBody(order.Id),
            sendAt: null,
            parentNotificationId: null,
            idempotencyKey: null,
            destinationOverride: null,
            cancellationToken);

        return order;
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (original is null)
        {
            throw new KeyNotFoundException($"Notification {notificationId} was not found.");
        }

        var existing = await _notifications.FirstOrDefaultAsync(
            new ResendByIdempotencySpec(notificationId, idempotencyKey), cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        if (original.ContentDisposed || string.IsNullOrWhiteSpace(original.Body))
        {
            throw new OrderStateException("The original message content is no longer available to resend.");
        }

        var resend = new OrderNotification(
            original.OrderId,
            original.BuyerId,
            NotificationKind.Resend,
            original.Destination,
            original.Body,
            original.Id,
            idempotencyKey);

        resend = await _notifications.AddAsync(resend, cancellationToken);

        try
        {
            var snapshot = await _smsGateway.SendImmediateAsync(original.Destination, original.Body, cancellationToken);
            resend.ApplyProviderState(
                snapshot.Sid,
                snapshot.Status,
                snapshot.ErrorCode,
                snapshot.ErrorMessage,
                snapshot.DateSent,
                snapshot.DateCreated,
                original.Body);
            if (!snapshot.Succeeded && snapshot.Sid is null)
            {
                resend.MarkSendFailed(snapshot.ErrorMessage ?? "The messaging provider rejected the send.");
            }

            await _notifications.UpdateAsync(resend, cancellationToken);
        }
        catch (Exception)
        {
            resend.MarkSendFailed("The messaging provider could not be reached.");
            await _notifications.UpdateAsync(resend, cancellationToken);
            _logger.LogWarning("Resend of notification {NotificationId} failed unexpectedly.", notificationId);
        }

        return resend;
    }

    public async Task<OrderNotification?> DisposeContentAsync(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(notification.ProviderSid) && !notification.ContentDisposed)
        {
            try
            {
                var snapshot = await _smsGateway.RedactBodyAsync(notification.ProviderSid, cancellationToken);
                notification.ApplyProviderState(
                    snapshot.Sid,
                    snapshot.Status,
                    snapshot.ErrorCode,
                    snapshot.ErrorMessage,
                    snapshot.DateSent,
                    snapshot.DateCreated,
                    null);
            }
            catch (Exception)
            {
                _logger.LogWarning(
                    "Provider content disposal failed for notification {NotificationId}; local content will still be removed.",
                    notification.Id);
            }
        }

        notification.MarkContentDisposed();
        await _notifications.UpdateAsync(notification, cancellationToken);
        return notification;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var providerMessages = await _smsGateway.ListSentFromAppAsync(from, to, cancellationToken);
        var truncated = providerMessages.Count >= 50 * 1000;

        var local = await _notifications.ListAsync(new NotificationsWithProviderSidSpec(), cancellationToken);
        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrWhiteSpace(m.Sid))
            .GroupBy(m => m.Sid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var matched = new List<ReconciliationRow>();
        var eShopOnly = new List<OrderNotification>();

        foreach (var notification in local)
        {
            if (string.IsNullOrWhiteSpace(notification.ProviderSid))
            {
                continue;
            }

            if (providerBySid.TryGetValue(notification.ProviderSid, out var provider))
            {
                matched.Add(new ReconciliationRow(notification, provider));
                providerBySid.Remove(notification.ProviderSid);
            }
            else if (IsInRange(notification, from, to))
            {
                eShopOnly.Add(notification);
            }
        }

        var providerOnly = providerBySid.Values.ToList();

        return new ReconciliationReport(
            from,
            to,
            FromNumber: _smsGateway.SendingNumber,
            matched,
            providerOnly,
            eShopOnly,
            truncated);
    }

    private static bool IsInRange(OrderNotification notification, DateTimeOffset from, DateTimeOffset to)
    {
        if (notification.CreatedAt >= from && notification.CreatedAt <= to)
        {
            return true;
        }

        if (notification.SendAt is DateTimeOffset sendAt && sendAt >= from && sendAt <= to)
        {
            return true;
        }

        return false;
    }
}
