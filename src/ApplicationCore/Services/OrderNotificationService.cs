using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates the SMS notifications for an order's lifecycle. Every send is best-effort: a
/// failure is recorded on the notification and swallowed, so it can never fail the order operation
/// that triggered it. A shopper with no number on file produces no notifications at all.
/// </summary>
public class OrderNotificationService : IOrderNotificationService
{
    /// <summary>How far ahead the post-delivery follow-up is queued with the provider.</summary>
    public static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly ISmsProvider _smsProvider;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        ISmsProvider smsProvider,
        IRepository<ContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        IAppLogger<OrderNotificationService> logger)
    {
        _smsProvider = smsProvider;
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken)
    {
        var recipients = await GetRecipientsAsync(order.BuyerId, cancellationToken);
        foreach (var recipient in recipients)
        {
            await SendImmediateAsync(order, recipient, NotificationType.OrderPlaced,
                NotificationMessages.OrderPlaced(order), cancellationToken);
        }
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken)
    {
        var recipients = await GetRecipientsAsync(order.BuyerId, cancellationToken);
        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);

        foreach (var recipient in recipients)
        {
            await SendImmediateAsync(order, recipient, NotificationType.OrderDispatched,
                NotificationMessages.OrderDispatched(order), cancellationToken);

            // Queue the "how did delivery go?" follow-up with the provider itself, to go out days
            // later. It is held by the provider, not by any timer in this application.
            await ScheduleFollowUpAsync(order, recipient, sendAt, cancellationToken);
        }
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken)
    {
        // Call off any follow-up that has not yet gone out first: a "how did delivery go?" text for
        // a cancelled order is exactly the incident this prevents. Done before the cancellation
        // message so a slow cancellation notice can never delay stopping the follow-up.
        await CancelPendingFollowUpsAsync(order, cancellationToken);

        var recipients = await GetRecipientsAsync(order.BuyerId, cancellationToken);
        foreach (var recipient in recipients)
        {
            await SendImmediateAsync(order, recipient, NotificationType.OrderCancelled,
                NotificationMessages.OrderCancelled(order), cancellationToken);
        }
    }

    public async Task RefreshDeliveryStateAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        if (notification.ProviderMessageSid is null)
            return;
        if (NotificationStatus.IsTerminal(notification.Status))
            return;

        try
        {
            var current = await _smsProvider.FetchAsync(notification.ProviderMessageSid, cancellationToken);
            if (!string.Equals(current.Status, notification.Status, StringComparison.Ordinal)
                || current.ErrorCode != notification.ErrorCode)
            {
                notification.UpdateDeliveryState(current.Status, current.ErrorCode);
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            // Reporting must not fail because the provider was briefly unreachable; keep the last
            // known state and move on.
            _logger.LogWarning("Could not refresh delivery state for notification {NotificationId}: {Error}",
                notification.Id, Describe(ex));
        }
    }

    public async Task<OrderNotification> ResendAsync(OrderNotification original, string idempotencyKey, CancellationToken cancellationToken)
    {
        // Idempotency: a repeat under the same key returns the message the first attempt produced.
        var existing = await _notifications.FirstOrDefaultAsync(
            new OrderNotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (existing is not null)
            return existing;

        // Re-send reuses the original message's body; if that content was disposed of, fall back to
        // a neutral line rather than resurrecting the redacted text.
        var body = original.Body ?? $"eShop: an update about your order #{original.OrderId}.";

        var resend = new OrderNotification(original.OrderId, original.BuyerId, NotificationType.Resend,
            original.Recipient, body);
        resend.SetIdempotencyKey(idempotencyKey);

        try
        {
            var result = await _smsProvider.SendAsync(original.Recipient, body, cancellationToken);
            resend.RecordSend(result.Sid, result.Status, result.ErrorCode);
        }
        catch (SmsProviderException ex)
        {
            _logger.LogWarning("Resend for order {OrderId} failed: {Reason}", original.OrderId, ex.Message);
            resend.RecordSendError(ex.ErrorCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Resend for order {OrderId} failed: {Error}", original.OrderId, Describe(ex));
            resend.RecordSendError();
        }

        await _notifications.AddAsync(resend, cancellationToken);
        return resend;
    }

    public async Task DisposeContentAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        // Redact at the provider first so the text is genuinely gone there, then clear it locally.
        // The record that a message was sent, and what became of it, is left intact.
        if (notification.ProviderMessageSid is not null && !notification.ContentDisposed)
        {
            await _smsProvider.RedactBodyAsync(notification.ProviderMessageSid, cancellationToken);
        }

        notification.DisposeContent();
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    private async Task<System.Collections.Generic.IReadOnlyList<ContactNumber>> GetRecipientsAsync(
        string buyerId, CancellationToken cancellationToken)
    {
        return await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
    }

    private async Task SendImmediateAsync(Order order, ContactNumber recipient, NotificationType type,
        string body, CancellationToken cancellationToken)
    {
        var notification = new OrderNotification(order.Id, order.BuyerId, type, recipient.PhoneNumber, body);

        try
        {
            var result = await _smsProvider.SendAsync(recipient.PhoneNumber, body, cancellationToken);
            notification.RecordSend(result.Sid, result.Status, result.ErrorCode);
            _logger.LogInformation("Sent {Type} notification for order {OrderId} (message {Sid}, status {Status}).",
                type, order.Id, result.Sid, result.Status);
        }
        catch (SmsProviderException ex)
        {
            _logger.LogWarning("Could not send {Type} notification for order {OrderId}: {Reason}",
                type, order.Id, ex.Message);
            notification.RecordSendError(ex.ErrorCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not send {Type} notification for order {OrderId}: {Error}",
                type, order.Id, Describe(ex));
            notification.RecordSendError();
        }

        await _notifications.AddAsync(notification, cancellationToken);
    }

    private async Task ScheduleFollowUpAsync(Order order, ContactNumber recipient, DateTimeOffset sendAt,
        CancellationToken cancellationToken)
    {
        var body = NotificationMessages.DeliveryFollowUp(order);
        var notification = new OrderNotification(order.Id, order.BuyerId, NotificationType.DeliveryFollowUp,
            recipient.PhoneNumber, body);

        try
        {
            var result = await _smsProvider.ScheduleAsync(recipient.PhoneNumber, body, sendAt, cancellationToken);
            notification.RecordSchedule(result.Sid, result.Status, sendAt);
            _logger.LogInformation("Scheduled delivery follow-up for order {OrderId} (message {Sid}) for {SendAt:u}.",
                order.Id, result.Sid, sendAt);
        }
        catch (SmsProviderException ex)
        {
            _logger.LogWarning("Could not schedule delivery follow-up for order {OrderId}: {Reason}",
                order.Id, ex.Message);
            notification.RecordSendError(ex.ErrorCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not schedule delivery follow-up for order {OrderId}: {Error}",
                order.Id, Describe(ex));
            notification.RecordSendError();
        }

        await _notifications.AddAsync(notification, cancellationToken);
    }

    private async Task CancelPendingFollowUpsAsync(Order order, CancellationToken cancellationToken)
    {
        var forOrder = await _notifications.ListAsync(
            new OrderNotificationsByOrderSpecification(order.Id), cancellationToken);

        var pendingFollowUps = forOrder.Where(n =>
            n.Type == NotificationType.DeliveryFollowUp
            && n.ProviderMessageSid is not null
            && !NotificationStatus.IsTerminal(n.Status));

        foreach (var followUp in pendingFollowUps)
        {
            try
            {
                await _smsProvider.CancelScheduledAsync(followUp.ProviderMessageSid!, cancellationToken);
                followUp.MarkCanceled();
                await _notifications.UpdateAsync(followUp, cancellationToken);
                _logger.LogInformation("Called off scheduled follow-up {NotificationId} for cancelled order {OrderId}.",
                    followUp.Id, order.Id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not call off follow-up {NotificationId} for order {OrderId}: {Error}",
                    followUp.Id, order.Id, Describe(ex));
            }
        }
    }

    /// <summary>PII-free description of an exception for logging.</summary>
    private static string Describe(Exception ex) =>
        ex is SmsProviderException ? ex.Message : ex.GetType().Name;
}
