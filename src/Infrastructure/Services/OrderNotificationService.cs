using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.Twilio;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class OrderNotificationService : IOrderNotificationService
{
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly ITwilioMessagingClient _messaging;
    private readonly IRepository<Order> _orders;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<ShopperContactNumber> _contactNumbers;
    private readonly IRepository<NotificationResendRecord> _resendRecords;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        ITwilioMessagingClient messaging,
        IRepository<Order> orders,
        IRepository<OrderNotification> notifications,
        IRepository<ShopperContactNumber> contactNumbers,
        IRepository<NotificationResendRecord> resendRecords,
        IOptions<TwilioSettings> options,
        IAppLogger<OrderNotificationService> logger)
    {
        _messaging = messaging;
        _orders = orders;
        _notifications = notifications;
        _contactNumbers = contactNumbers;
        _resendRecords = resendRecords;
        _settings = options.Value;
        _logger = logger;
    }

    public Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"eShopOnWeb: Your order #{order.Id} has been placed. Total {order.Total().ToString("0.00", CultureInfo.InvariantCulture)}.";
        return TryNotifyAsync(order, NotificationKind.OrderPlaced, body, sendAt: null, parentNotificationId: null, cancellationToken);
    }

    public async Task DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        try
        {
            order.MarkDispatched();
        }
        catch (InvalidOperationException ex)
        {
            throw new OrderStateException(ex.Message);
        }

        await _orders.UpdateAsync(order, cancellationToken);

        await TryNotifyAsync(
            order,
            NotificationKind.OrderDispatched,
            $"eShopOnWeb: Order #{order.Id} is on its way.",
            sendAt: null,
            parentNotificationId: null,
            cancellationToken);

        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        await TryNotifyAsync(
            order,
            NotificationKind.DeliveryFollowUp,
            $"eShopOnWeb: How did delivery of order #{order.Id} go? Reply with your feedback.",
            sendAt,
            parentNotificationId: null,
            cancellationToken);
    }

    public async Task CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        try
        {
            order.Cancel();
        }
        catch (InvalidOperationException ex)
        {
            throw new OrderStateException(ex.Message);
        }

        await _orders.UpdateAsync(order, cancellationToken);

        await CancelPendingFollowUpsAsync(order.Id, cancellationToken);

        await TryNotifyAsync(
            order,
            NotificationKind.OrderCancelled,
            $"eShopOnWeb: Order #{order.Id} has been cancelled.",
            sendAt: null,
            parentNotificationId: null,
            cancellationToken);
    }

    public async Task<IReadOnlyList<Order>> GetBuyerOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var notifications = await _notifications.ListAsync(new OrderNotificationsByBuyerIdSpec(buyerId), cancellationToken);
        await RefreshAsync(notifications, cancellationToken);
        return orders;
    }

    public async Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(int orderId, string buyerId, bool isAdministrator, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        if (!isAdministrator && order.BuyerId != buyerId)
        {
            throw new KeyNotFoundException("Order was not found.");
        }

        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderIdSpec(orderId), cancellationToken);
        await RefreshAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("An idempotency key is required.", nameof(idempotencyKey));
        }

        var existingAttempt = await _resendRecords.FirstOrDefaultAsync(
            new NotificationResendByKeySpec(notificationId, idempotencyKey), cancellationToken);
        if (existingAttempt != null)
        {
            var existing = await _notifications.GetByIdAsync(existingAttempt.ResultNotificationId, cancellationToken);
            if (existing != null)
            {
                await RefreshAsync(new[] { existing }, cancellationToken);
                return existing;
            }
        }

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new KeyNotFoundException("Notification was not found.");

        await RefreshAsync(new[] { original }, cancellationToken);

        if (!original.DidNotReachShopper())
        {
            throw new OrderStateException("This notification already reached the shopper and will not be resent.");
        }

        if (original.ContentDisposed || string.IsNullOrEmpty(original.Body))
        {
            throw new OrderStateException("The content of this notification has been disposed and cannot be resent.");
        }

        var destination = await ResolveDestinationForResendAsync(original, cancellationToken);
        if (destination == null)
        {
            throw new OrderStateException("No registered contact number is available for a resend.");
        }

        var resend = new OrderNotification(
            original.OrderId,
            original.BuyerId,
            original.Kind,
            destination,
            original.Body,
            sendAt: null,
            parentNotificationId: original.Id);

        await SendOrScheduleAsync(resend, cancellationToken);
        resend = await _notifications.AddAsync(resend, cancellationToken);

        await _resendRecords.AddAsync(new NotificationResendRecord(original.Id, idempotencyKey, resend.Id), cancellationToken);
        return resend;
    }

    public async Task DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new KeyNotFoundException("Notification was not found.");

        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            try
            {
                var redacted = await _messaging.RedactBodyAsync(notification.ProviderMessageSid, cancellationToken);
                notification.ApplyProviderState(redacted.Status, redacted.ErrorCode, redacted.Body);
            }
            catch (Exception ex) when (ex is TwilioRequestException or HttpRequestException or TaskCanceledException)
            {
                _logger.LogError(ex, "Failed to redact provider content for notification {NotificationId}.", notification.Id);
                throw;
            }
        }

        notification.MarkContentDisposed();
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    public async Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new ArgumentException("The 'to' timestamp must be on or after 'from'.");
        }

        var providerMessages = await _messaging.ListSentFromAsync(_settings.FromNumber, from, to, cancellationToken);
        var applicationMessages = await _notifications.ListAsync(new OrderNotificationsInRangeSpec(from, to), cancellationToken);
        await RefreshAsync(applicationMessages, cancellationToken);

        var providerBySid = providerMessages
            .Where(message => !string.IsNullOrEmpty(message.Sid))
            .GroupBy(message => message.Sid)
            .ToDictionary(group => group.Key, group => group.First());

        var applicationBySid = applicationMessages
            .Where(message => !string.IsNullOrEmpty(message.ProviderMessageSid))
            .GroupBy(message => message.ProviderMessageSid!)
            .ToDictionary(group => group.Key, group => group.First());

        var matched = new List<ReconciledMessage>();
        var providerOnly = new List<ReconciledMessage>();
        var applicationOnly = new List<ReconciledMessage>();

        foreach (var (sid, provider) in providerBySid)
        {
            if (applicationBySid.TryGetValue(sid, out var local))
            {
                matched.Add(ToReconciled(local, provider));
            }
            else
            {
                providerOnly.Add(new ReconciledMessage
                {
                    ProviderMessageSid = provider.Sid,
                    ProviderStatus = provider.Status,
                    ProviderDateSent = provider.DateSent ?? provider.DateCreated
                });
            }
        }

        foreach (var local in applicationMessages)
        {
            if (string.IsNullOrEmpty(local.ProviderMessageSid) || !providerBySid.ContainsKey(local.ProviderMessageSid))
            {
                applicationOnly.Add(ToReconciled(local, provider: null));
            }
        }

        return new NotificationReconciliationReport
        {
            From = from,
            To = to,
            FromNumber = _settings.FromNumber,
            Matched = matched,
            ProviderOnly = providerOnly,
            ApplicationOnly = applicationOnly
        };
    }

    private async Task TryNotifyAsync(
        Order order,
        NotificationKind kind,
        string body,
        DateTimeOffset? sendAt,
        int? parentNotificationId,
        CancellationToken cancellationToken)
    {
        var destination = await GetCurrentDestinationAsync(order.BuyerId, cancellationToken);
        if (destination == null)
        {
            _logger.LogInformation("Skipping {Kind} notification for order {OrderId}; shopper has no contact number.", kind, order.Id);
            return;
        }

        var notification = new OrderNotification(order.Id, order.BuyerId, kind, destination, body, sendAt, parentNotificationId);
        await SendOrScheduleAsync(notification, cancellationToken);
        await _notifications.AddAsync(notification, cancellationToken);
    }

    private async Task SendOrScheduleAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            TwilioMessageSnapshot snapshot;
            if (notification.SendAt.HasValue)
            {
                snapshot = await _messaging.ScheduleAsync(notification.DestinationNumber, notification.Body ?? string.Empty, notification.SendAt.Value, cancellationToken);
            }
            else
            {
                snapshot = await _messaging.SendAsync(notification.DestinationNumber, notification.Body ?? string.Empty, cancellationToken);
            }

            notification.RecordAccepted(snapshot.Sid, snapshot.Status, snapshot.ErrorCode);
        }
        catch (Exception ex) when (ex is TwilioRequestException or HttpRequestException or TaskCanceledException)
        {
            _logger.LogError(ex, "Failed to send {Kind} notification {NotificationId} for order {OrderId}.", notification.Kind, notification.Id, notification.OrderId);
            notification.RecordLocalFailure("provider_rejected");
        }
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var followUps = await _notifications.ListAsync(new ScheduledFollowUpNotificationsSpec(orderId), cancellationToken);
        foreach (var followUp in followUps)
        {
            if (string.IsNullOrEmpty(followUp.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var cancelled = await _messaging.CancelAsync(followUp.ProviderMessageSid, cancellationToken);
                followUp.ApplyProviderState(cancelled.Status, cancelled.ErrorCode, cancelled.Body);
                await _notifications.UpdateAsync(followUp, cancellationToken);
            }
            catch (Exception ex) when (ex is TwilioRequestException or HttpRequestException or TaskCanceledException)
            {
                _logger.LogError(ex, "Failed to cancel follow-up notification {NotificationId} for order {OrderId}.", followUp.Id, orderId);
            }
        }
    }

    private async Task RefreshAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var snapshot = await _messaging.FetchAsync(notification.ProviderMessageSid, cancellationToken);
                notification.ApplyProviderState(snapshot.Status, snapshot.ErrorCode, snapshot.Body);
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex) when (ex is TwilioRequestException or HttpRequestException or TaskCanceledException)
            {
                _logger.LogWarning("Could not refresh provider status for notification {NotificationId}.", notification.Id);
            }
        }
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order == null)
        {
            throw new KeyNotFoundException("Order was not found.");
        }

        return order;
    }

    private async Task<string?> GetCurrentDestinationAsync(string buyerId, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ShopperContactNumbersByBuyerIdSpec(buyerId), cancellationToken);
        return numbers.FirstOrDefault()?.PhoneNumber;
    }

    private async Task<string?> ResolveDestinationForResendAsync(OrderNotification original, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ShopperContactNumbersByBuyerIdSpec(original.BuyerId), cancellationToken);
        if (numbers.Any(number => number.PhoneNumber == original.DestinationNumber))
        {
            return original.DestinationNumber;
        }

        return numbers.FirstOrDefault()?.PhoneNumber;
    }

    private static ReconciledMessage ToReconciled(OrderNotification local, TwilioMessageSnapshot? provider)
    {
        return new ReconciledMessage
        {
            NotificationId = local.Id,
            ProviderMessageSid = local.ProviderMessageSid ?? provider?.Sid,
            ProviderStatus = provider?.Status,
            ApplicationStatus = local.ProviderStatus,
            ProviderDateSent = provider?.DateSent ?? provider?.DateCreated,
            ApplicationCreatedAt = local.CreatedAt
        };
    }
}
