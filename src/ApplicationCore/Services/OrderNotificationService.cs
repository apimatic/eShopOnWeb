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

/// <summary>
/// Orchestrates the SMS messages that go out as an order moves. Sends are best-effort: a failure
/// to message the shopper is recorded against the notification but never propagates to the caller,
/// so the order is still placed, dispatched or cancelled and the request still succeeds.
/// </summary>
public class OrderNotificationService : IOrderNotificationService
{
    /// <summary>How far after dispatch the "how did delivery go?" follow-up is queued for.</summary>
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IReadRepository<ContactNumber> _contactNumberRepository;
    private readonly ISmsGateway _smsGateway;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<OrderNotification> notificationRepository,
        IReadRepository<ContactNumber> contactNumberRepository,
        ISmsGateway smsGateway,
        IAppLogger<OrderNotificationService> logger)
    {
        _notificationRepository = notificationRepository;
        _contactNumberRepository = contactNumberRepository;
        _smsGateway = smsGateway;
        _logger = logger;
    }

    public async Task<IReadOnlyList<OrderNotification>> NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var toNumber = await ResolveContactNumberAsync(order.BuyerId, cancellationToken);
        if (toNumber is null)
        {
            _logger.LogInformation("Order {0} placed for a shopper with no number on file; not messaged.", order.Id);
            return Array.Empty<OrderNotification>();
        }

        var body = $"eShop: your order #{order.Id} has been placed. Thank you for shopping with us!";
        var notification = await SendImmediateAsync(order, NotificationType.OrderPlaced, toNumber, body, cancellationToken);
        return new[] { notification };
    }

    public async Task<IReadOnlyList<OrderNotification>> NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var toNumber = await ResolveContactNumberAsync(order.BuyerId, cancellationToken);
        if (toNumber is null)
        {
            _logger.LogInformation("Order {0} dispatched for a shopper with no number on file; not messaged.", order.Id);
            return Array.Empty<OrderNotification>();
        }

        var created = new List<OrderNotification>();

        var dispatchBody = $"eShop: good news - your order #{order.Id} is on its way!";
        created.Add(await SendImmediateAsync(order, NotificationType.OrderDispatched, toNumber, dispatchBody, cancellationToken));

        // Queue the delivery follow-up with the provider for a few days later. It is held by the
        // provider, not by this application, so no timer of our own has to fire it.
        var followUpBody = $"eShop: how did the delivery of your order #{order.Id} go? We'd love your feedback.";
        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        var followUp = new OrderNotification(order.Id, order.BuyerId, NotificationType.DeliveryFollowUp, toNumber, followUpBody);
        try
        {
            var result = await _smsGateway.ScheduleAsync(toNumber, followUpBody, sendAt, cancellationToken);
            followUp.RecordAccepted(result.Sid, result.Status, result.ErrorCode, result.ErrorMessage, sendAt);
        }
        catch (Exception ex)
        {
            followUp.RecordSendError(ex.Message);
            _logger.LogWarning("Failed to schedule delivery follow-up for order {0}: {1}", order.Id, LogSanitizer.RedactPhoneNumbers(ex.Message));
        }

        await _notificationRepository.AddAsync(followUp, cancellationToken);
        created.Add(followUp);
        return created;
    }

    public async Task<IReadOnlyList<OrderNotification>> NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        // Call off any follow-up the provider still holds, regardless of whether the shopper still
        // has a number on file. Asking how a delivery went for a cancelled order is the incident to prevent.
        var scheduledFollowUps = await _notificationRepository.ListAsync(new ScheduledFollowUpsByOrderSpecification(order.Id), cancellationToken);
        foreach (var followUp in scheduledFollowUps)
        {
            try
            {
                if (followUp.ProviderMessageSid is not null)
                {
                    await _smsGateway.CancelScheduledAsync(followUp.ProviderMessageSid, cancellationToken);
                }

                followUp.MarkCanceled();
                await _notificationRepository.UpdateAsync(followUp, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to cancel scheduled follow-up {0} for order {1}: {2}", followUp.Id, order.Id, LogSanitizer.RedactPhoneNumbers(ex.Message));
            }
        }

        var toNumber = await ResolveContactNumberAsync(order.BuyerId, cancellationToken);
        if (toNumber is null)
        {
            _logger.LogInformation("Order {0} cancelled for a shopper with no number on file; not messaged.", order.Id);
            return Array.Empty<OrderNotification>();
        }

        var body = $"eShop: your order #{order.Id} has been cancelled. If this is unexpected, please contact us.";
        var notification = await SendImmediateAsync(order, NotificationType.OrderCancelled, toNumber, body, cancellationToken);
        return new[] { notification };
    }

    public async Task RefreshStatusesAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken = default)
    {
        foreach (var notification in notifications)
        {
            if (notification.ProviderMessageSid is null)
            {
                continue;
            }

            try
            {
                var state = await _smsGateway.GetMessageAsync(notification.ProviderMessageSid, cancellationToken);
                if (state is not null)
                {
                    notification.UpdateProviderStatus(state.Status, state.ErrorCode, state.ErrorMessage);
                    await _notificationRepository.UpdateAsync(notification, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to refresh status for notification {0}: {1}", notification.Id, LogSanitizer.RedactPhoneNumbers(ex.Message));
            }
        }
    }

    public async Task<ResendResult> ResendAsync(OrderNotification original, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        // Idempotency is an application-side concern (the provider offers no key on message create):
        // a repeat under the same key returns the message already produced, without sending again.
        var existing = await _notificationRepository.FirstOrDefaultAsync(
            new OrderNotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (existing is not null)
        {
            return new ResendResult(existing, Deduplicated: true);
        }

        if (string.IsNullOrEmpty(original.ToNumber))
        {
            throw new SmsGatewayException("The original message has no recipient on record to resend to.");
        }

        if (original.ContentRedacted || string.IsNullOrEmpty(original.Body))
        {
            throw new SmsGatewayException("The original message content has been disposed of and cannot be resent.");
        }

        var resend = new OrderNotification(original.OrderId, original.OwnerId, original.Type, original.ToNumber, original.Body);
        resend.SetIdempotencyKey(idempotencyKey);
        try
        {
            var result = await _smsGateway.SendAsync(original.ToNumber, original.Body, cancellationToken);
            resend.RecordAccepted(result.Sid, result.Status, result.ErrorCode, result.ErrorMessage);
        }
        catch (Exception ex)
        {
            resend.RecordSendError(ex.Message);
            _logger.LogWarning("Resend for order {0} could not be handed to the provider: {1}", original.OrderId, LogSanitizer.RedactPhoneNumbers(ex.Message));
        }

        await _notificationRepository.AddAsync(resend, cancellationToken);
        return new ResendResult(resend, Deduplicated: false);
    }

    public async Task DisposeContentAsync(OrderNotification notification, CancellationToken cancellationToken = default)
    {
        // The body must be gone from the provider too, not merely hidden here. If the provider still
        // holds the message, redact it there first; a failure to do so surfaces to the caller.
        if (notification.ProviderMessageSid is not null)
        {
            await _smsGateway.RedactBodyAsync(notification.ProviderMessageSid, cancellationToken);
        }

        notification.RedactContent();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
    }

    private async Task<OrderNotification> SendImmediateAsync(Order order, NotificationType type, string toNumber, string body, CancellationToken cancellationToken)
    {
        var notification = new OrderNotification(order.Id, order.BuyerId, type, toNumber, body);
        try
        {
            var result = await _smsGateway.SendAsync(toNumber, body, cancellationToken);
            notification.RecordAccepted(result.Sid, result.Status, result.ErrorCode, result.ErrorMessage);
        }
        catch (Exception ex)
        {
            notification.RecordSendError(ex.Message);
            _logger.LogWarning("Notification of type {0} for order {1} could not be handed to the provider: {2}", type, order.Id, LogSanitizer.RedactPhoneNumbers(ex.Message));
        }

        await _notificationRepository.AddAsync(notification, cancellationToken);
        return notification;
    }

    private async Task<string?> ResolveContactNumberAsync(string ownerId, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumberRepository.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), cancellationToken);
        return numbers.Count > 0 ? numbers[0].PhoneNumber : null;
    }
}
