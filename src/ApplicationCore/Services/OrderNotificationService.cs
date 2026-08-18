using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Sends the shopper the SMS messages that accompany an order's lifecycle, and records every message as a
/// <see cref="Notification"/> so the operator can later see, resend or reconcile it.
///
/// Guarantee: no method here ever throws because of a messaging problem. A send that fails is recorded (as a
/// failed notification) and swallowed, so placing / dispatching / cancelling an order always succeeds. A
/// shopper with no number on file is simply not messaged.
/// </summary>
public class OrderNotificationService : IOrderNotificationService
{
    private readonly IReadRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<Notification> _notifications;
    private readonly ISmsProvider _smsProvider;
    private readonly IAppLogger<OrderNotificationService> _logger;
    private readonly OrderNotificationOptions _options;

    public OrderNotificationService(
        IReadRepository<ContactNumber> contactNumbers,
        IRepository<Notification> notifications,
        ISmsProvider smsProvider,
        IAppLogger<OrderNotificationService> logger,
        OrderNotificationOptions options)
    {
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _smsProvider = smsProvider;
        _logger = logger;
        _options = options;
    }

    public async Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken)
    {
        var body = $"Your eShop order #{order.Id} has been placed. Thanks for shopping with us!";
        await SendImmediateToAllAsync(order, NotificationType.OrderPlaced, body, cancellationToken);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken)
    {
        var dispatchBody = $"Good news! Your eShop order #{order.Id} is on its way.";
        await SendImmediateToAllAsync(order, NotificationType.OrderDispatched, dispatchBody, cancellationToken);

        // Queue a "how did the delivery go?" follow-up WITH THE PROVIDER for a few days later — not held here.
        var followUpBody = $"How did the delivery of your eShop order #{order.Id} go? We'd love your feedback.";
        var sendAt = DateTimeOffset.UtcNow.Add(_options.FollowUpDelay);
        await ScheduleFollowUpToAllAsync(order, followUpBody, sendAt, cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken)
    {
        // Call off any not-yet-sent delivery-feedback follow-up FIRST, so a cancelled order can never trigger a
        // "how did delivery go?" message.
        await CancelPendingFollowUpsAsync(order, cancellationToken);

        var body = $"Your eShop order #{order.Id} has been cancelled. If this is unexpected, please contact support.";
        await SendImmediateToAllAsync(order, NotificationType.OrderCancelled, body, cancellationToken);
    }

    private async Task SendImmediateToAllAsync(Order order, NotificationType type, string body, CancellationToken cancellationToken)
    {
        foreach (var recipient in await GetRecipientsAsync(order, cancellationToken))
        {
            var notification = Notification.CreateImmediate(order.Id, order.BuyerId, recipient, type, body);
            try
            {
                var result = await _smsProvider.SendAsync(recipient, body, cancellationToken);
                notification.MarkAccepted(result.Sid, result.Status);
            }
            catch (Exception ex)
            {
                // A send failure must never fail the underlying order operation — record it and move on.
                notification.MarkSendFailed(Describe(ex));
                _logger.LogWarning("Order {OrderId}: {Type} SMS could not be sent: {Reason}", order.Id, type, Describe(ex));
            }

            await SafeAddAsync(notification, cancellationToken);
        }
    }

    private async Task ScheduleFollowUpToAllAsync(Order order, string body, DateTimeOffset sendAt, CancellationToken cancellationToken)
    {
        foreach (var recipient in await GetRecipientsAsync(order, cancellationToken))
        {
            var notification = Notification.CreateScheduled(order.Id, order.BuyerId, recipient, NotificationType.DeliveryFeedback, body, sendAt);
            try
            {
                var result = await _smsProvider.ScheduleAsync(recipient, body, sendAt, cancellationToken);
                notification.MarkAccepted(result.Sid, result.Status);
            }
            catch (Exception ex)
            {
                notification.MarkSendFailed(Describe(ex));
                _logger.LogWarning("Order {OrderId}: delivery-feedback follow-up could not be scheduled: {Reason}", order.Id, Describe(ex));
            }

            await SafeAddAsync(notification, cancellationToken);
        }
    }

    private async Task CancelPendingFollowUpsAsync(Order order, CancellationToken cancellationToken)
    {
        try
        {
            var pending = await _notifications.ListAsync(new ScheduledFollowUpsByOrderSpecification(order.Id), cancellationToken);
            foreach (var followUp in pending)
            {
                try
                {
                    await _smsProvider.CancelScheduledAsync(followUp.ProviderSid!, cancellationToken);
                    followUp.MarkCanceled();
                    await _notifications.UpdateAsync(followUp, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Order {OrderId}: a scheduled follow-up could not be cancelled: {Reason}", order.Id, Describe(ex));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Order {OrderId}: could not enumerate scheduled follow-ups to cancel: {Reason}", order.Id, Describe(ex));
        }
    }

    private async Task<System.Collections.Generic.IReadOnlyList<string>> GetRecipientsAsync(Order order, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(order.BuyerId), cancellationToken);
        var recipients = new System.Collections.Generic.List<string>(numbers.Count);
        foreach (var n in numbers)
        {
            recipients.Add(n.PhoneNumber);
        }
        return recipients;
    }

    private async Task SafeAddAsync(Notification notification, CancellationToken cancellationToken)
    {
        try
        {
            await _notifications.AddAsync(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            // Persisting the audit record must not fail the order operation either.
            _logger.LogWarning("Order {OrderId}: could not persist a notification record: {Reason}", notification.OrderId, Describe(ex));
        }
    }

    // A caller-safe description that never contains a phone number.
    private static string Describe(Exception ex) => ex.Message;
}
