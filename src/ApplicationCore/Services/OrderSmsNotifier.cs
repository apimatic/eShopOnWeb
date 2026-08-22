using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderSmsNotifier : IOrderSmsNotifier
{
    private readonly IRepository<ShopperContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly ISmsGateway _smsGateway;
    private readonly IAppLogger<OrderSmsNotifier> _logger;

    public OrderSmsNotifier(
        IRepository<ShopperContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        ISmsGateway smsGateway,
        IAppLogger<OrderSmsNotifier> logger)
    {
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _smsGateway = smsGateway;
        _logger = logger;
    }

    public static string BodyFor(OrderNotificationKind kind, int orderId) => kind switch
    {
        OrderNotificationKind.OrderPlaced => $"Your eShopOnWeb order #{orderId} has been placed. Thank you!",
        OrderNotificationKind.OrderDispatched => $"Your eShopOnWeb order #{orderId} is on its way.",
        OrderNotificationKind.DeliveryFollowUp => $"How did the delivery of your eShopOnWeb order #{orderId} go?",
        OrderNotificationKind.OrderCancelled => $"Your eShopOnWeb order #{orderId} has been cancelled.",
        _ => $"Update on your eShopOnWeb order #{orderId}."
    };

    public async Task NotifyAsync(
        int orderId,
        string buyerId,
        OrderNotificationKind kind,
        CancellationToken cancellationToken,
        DateTimeOffset? scheduleAt = null)
    {
        IReadOnlyList<ShopperContactNumber> numbers;
        try
        {
            numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpec(buyerId), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to load contact numbers for order {OrderId}: {ExceptionType}", orderId, ex.GetType().Name);
            return;
        }

        if (numbers.Count == 0)
        {
            return;
        }

        var body = BodyFor(kind, orderId);
        foreach (var number in numbers)
        {
            var notification = new OrderNotification(orderId, buyerId, kind, body, number.CanonicalNumber);
            if (scheduleAt is not null)
            {
                notification.MarkScheduledFor(scheduleAt.Value);
            }

            try
            {
                var send = scheduleAt is null
                    ? await _smsGateway.SendSmsAsync(number.CanonicalNumber, body, cancellationToken)
                    : await _smsGateway.ScheduleSmsAsync(number.CanonicalNumber, body, scheduleAt.Value, cancellationToken);

                if (send.AcceptedByProvider)
                {
                    notification.RecordProviderAcceptance(send.ProviderSid, send.Status, send.ErrorCode, send.ErrorMessage);
                }
                else
                {
                    notification.RecordSendFailure(send.ErrorMessage ?? "The provider did not accept the message.");
                    _logger.LogWarning(
                        "SMS for order {OrderId} kind {Kind} was not accepted. Status {Status}",
                        orderId,
                        kind,
                        send.Status ?? "unknown");
                }
            }
            catch (Exception ex)
            {
                notification.RecordSendFailure("The provider could not send the message.");
                _logger.LogWarning("SMS for order {OrderId} kind {Kind} failed with {ExceptionType}", orderId, kind, ex.GetType().Name);
            }

            try
            {
                await _notifications.AddAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to persist SMS notification for order {OrderId}: {ExceptionType}", orderId, ex.GetType().Name);
            }
        }
    }

    public async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var pending = await _notifications.ListAsync(new PendingFollowUpNotificationsSpec(orderId), cancellationToken);
        foreach (var notification in pending)
        {
            if (string.IsNullOrWhiteSpace(notification.ProviderSid))
            {
                continue;
            }

            try
            {
                var result = await _smsGateway.CancelScheduledAsync(notification.ProviderSid, cancellationToken);
                if (result.AcceptedByProvider)
                {
                    notification.RecordProviderAcceptance(result.ProviderSid ?? notification.ProviderSid, result.Status, result.ErrorCode, result.ErrorMessage);
                }
                else
                {
                    _logger.LogWarning(
                        "Could not cancel scheduled follow-up {NotificationId} for order {OrderId}: {Message}",
                        notification.Id,
                        orderId,
                        result.ErrorMessage is null ? "unknown" : "provider error");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Could not cancel scheduled follow-up {NotificationId} for order {OrderId}: {ExceptionType}",
                    notification.Id,
                    orderId,
                    ex.GetType().Name);
            }

            await _notifications.UpdateAsync(notification, cancellationToken);
        }
    }

    public async Task RefreshAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrWhiteSpace(notification.ProviderSid))
            {
                continue;
            }

            try
            {
                var snapshot = await _smsGateway.FetchAsync(notification.ProviderSid, cancellationToken);
                if (snapshot is null)
                {
                    continue;
                }

                notification.ApplyProviderSnapshot(snapshot.Status, snapshot.ErrorCode, snapshot.ErrorMessage, snapshot.Body);
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Failed to refresh notification {NotificationId}: {ExceptionType}",
                    notification.Id,
                    ex.GetType().Name);
            }
        }
    }
}
