using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly ISmsProvider _smsProvider;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<ContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        ISmsProvider smsProvider,
        IAppLogger<OrderNotificationService> logger)
    {
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _smsProvider = smsProvider;
        _logger = logger;
    }

    public async Task NotifyOrderPlacedAsync(Order order, CancellationToken ct = default)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(order.BuyerId), ct);
        foreach (var number in numbers)
        {
            await SendAndRecordAsync(order, number, NotificationKind.OrderPlaced,
                NotificationMessages.OrderPlaced(order), scheduledFor: null, ct);
        }
    }

    public async Task NotifyOrderDispatchedAsync(Order order, TimeSpan followUpDelay, CancellationToken ct = default)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(order.BuyerId), ct);
        var followUpAt = DateTimeOffset.UtcNow.Add(followUpDelay);

        foreach (var number in numbers)
        {
            await SendAndRecordAsync(order, number, NotificationKind.OrderDispatched,
                NotificationMessages.OrderDispatched(order), scheduledFor: null, ct);

            // The follow-up is queued with the provider itself for a few days later.
            var followUp = new OrderNotification(order.Id, order.BuyerId, number.Id, number.PhoneNumber,
                NotificationKind.DeliveryFollowUp, NotificationMessages.DeliveryFollowUp(order), followUpAt);
            await _notifications.AddAsync(followUp, ct);
            try
            {
                var result = await _smsProvider.ScheduleAsync(number.PhoneNumber, followUp.Body!, followUpAt, ct);
                followUp.MarkAccepted(result.ProviderMessageSid, result.ProviderStatus);
            }
            catch (Exception ex)
            {
                // Never fail the dispatch because a message could not be queued.
                followUp.MarkSendFailed(SafeError(ex));
                _logger.LogWarning("Delivery follow-up for order {OrderId} (notification {NotificationId}) could not be queued: {Error}",
                    order.Id, followUp.Id, SafeError(ex));
            }
            await _notifications.UpdateAsync(followUp, ct);
        }
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken ct = default)
    {
        // Call off any follow-up the provider has not sent yet — a cancelled order's
        // "how did the delivery go?" must never reach the shopper.
        var scheduled = await _notifications.ListAsync(new ScheduledNotificationsByOrderSpecification(order.Id), ct);
        foreach (var followUp in scheduled)
        {
            try
            {
                await _smsProvider.CancelScheduledAsync(followUp.ProviderMessageSid!, ct);
                followUp.UpdateProviderState(NotificationStatus.Canceled, null, null);
            }
            catch (SmsProviderException ex)
            {
                _logger.LogWarning("Cancelling scheduled notification {NotificationId} failed ({StatusCode}); settling state from the provider.",
                    followUp.Id, ex.StatusCode);
                await SettleFromProviderAsync(followUp, ct);
            }
            await _notifications.UpdateAsync(followUp, ct);
        }

        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(order.BuyerId), ct);
        foreach (var number in numbers)
        {
            await SendAndRecordAsync(order, number, NotificationKind.OrderCancelled,
                NotificationMessages.OrderCancelled(order), scheduledFor: null, ct);
        }
    }

    public async Task RefreshAsync(IReadOnlyCollection<OrderNotification> notifications, CancellationToken ct = default)
    {
        foreach (var notification in notifications)
        {
            if (notification.ProviderMessageSid is null || NotificationStatus.IsTerminal(notification.Status))
                continue;

            try
            {
                var state = await _smsProvider.GetMessageStateAsync(notification.ProviderMessageSid, ct);
                notification.UpdateProviderState(state.Status, state.ErrorCode, state.ErrorMessage);
                if (state.Body is not null && state.Body.Length == 0 && !notification.ContentRedacted)
                {
                    // Provider no longer holds the body; keep the local record consistent.
                    notification.RedactContent();
                }
                await _notifications.UpdateAsync(notification, ct);
            }
            catch (Exception ex) when (ex is SmsProviderException or System.Net.Http.HttpRequestException or TaskCanceledException)
            {
                // Best effort: keep the last known state.
                _logger.LogWarning("Could not refresh notification {NotificationId}: {Error}", notification.Id, SafeError(ex));
            }
        }
    }

    public async Task<ResendNotificationResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct = default)
    {
        var existing = await _notifications.FirstOrDefaultAsync(new NotificationByIdempotencyKeySpecification(idempotencyKey), ct);
        if (existing is not null)
        {
            return new ResendNotificationResult(ResendOutcome.DuplicateIdempotencyKey, existing, null);
        }

        var original = await _notifications.GetByIdAsync(notificationId, ct);
        if (original is null)
        {
            return new ResendNotificationResult(ResendOutcome.NotificationNotFound, null, null);
        }
        if (original.ContactNumberId is null)
        {
            // The shopper removed the number: nothing may be sent to it again.
            return new ResendNotificationResult(ResendOutcome.ContactNumberRemoved, null, "The contact number this message went to has been removed.");
        }
        if (original.ContentRedacted || original.Body is null)
        {
            return new ResendNotificationResult(ResendOutcome.ContentRedacted, null, "The content of this message has been disposed of.");
        }

        var contactNumber = await _contactNumbers.GetByIdAsync(original.ContactNumberId.Value, ct);
        if (contactNumber is null)
        {
            return new ResendNotificationResult(ResendOutcome.ContactNumberRemoved, null, "The contact number this message went to has been removed.");
        }

        var resend = new OrderNotification(original.OrderId, original.BuyerId, contactNumber.Id,
            contactNumber.PhoneNumber, original.Kind, original.Body,
            scheduledFor: null, idempotencyKey: idempotencyKey, resendOfNotificationId: original.Id);
        await _notifications.AddAsync(resend, ct);

        try
        {
            var result = await _smsProvider.SendAsync(contactNumber.PhoneNumber, original.Body, ct);
            resend.MarkAccepted(result.ProviderMessageSid, result.ProviderStatus);
        }
        catch (SmsProviderException ex)
        {
            resend.MarkSendFailed(SafeError(ex));
            await _notifications.UpdateAsync(resend, ct);
            return new ResendNotificationResult(ResendOutcome.ProviderRejected, resend, "The provider could not send the message.");
        }

        await _notifications.UpdateAsync(resend, ct);
        return new ResendNotificationResult(ResendOutcome.Resent, resend, null);
    }

    private async Task SendAndRecordAsync(Order order, ContactNumber number, NotificationKind kind,
        string body, DateTimeOffset? scheduledFor, CancellationToken ct)
    {
        var notification = new OrderNotification(order.Id, order.BuyerId, number.Id, number.PhoneNumber,
            kind, body, scheduledFor);
        await _notifications.AddAsync(notification, ct);

        try
        {
            var result = await _smsProvider.SendAsync(number.PhoneNumber, body, ct);
            notification.MarkAccepted(result.ProviderMessageSid, result.ProviderStatus);
        }
        catch (Exception ex) when (ex is SmsProviderException or System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            // A message that cannot be sent never fails the underlying operation.
            notification.MarkSendFailed(SafeError(ex));
            _logger.LogWarning("Notification {NotificationId} for order {OrderId} could not be sent: {Error}",
                notification.Id, order.Id, SafeError(ex));
        }

        await _notifications.UpdateAsync(notification, ct);
    }

    private async Task SettleFromProviderAsync(OrderNotification notification, CancellationToken ct)
    {
        try
        {
            var state = await _smsProvider.GetMessageStateAsync(notification.ProviderMessageSid!, ct);
            notification.UpdateProviderState(state.Status, state.ErrorCode, state.ErrorMessage);
        }
        catch (SmsProviderException ex)
        {
            _logger.LogWarning("Could not settle notification {NotificationId} from the provider: {Error}",
                notification.Id, SafeError(ex));
        }
    }

    // Error text is safe to log; a shopper's number never is — it is never included here.
    private static string SafeError(Exception ex) => ex is SmsProviderException spe
        ? $"provider status {(int?)spe.StatusCode}: {spe.Message}"
        : ex.Message;
}
