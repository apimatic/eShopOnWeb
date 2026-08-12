using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates order notifications on top of the <see cref="ISmsNotificationProvider"/> seam. Sending
/// is always best-effort: failures are recorded on the notification but never propagate to the caller's
/// order operation. Destination numbers and message bodies are personal data and are never logged.
/// </summary>
public class OrderNotificationService : IOrderNotificationService
{
    // The follow-up "how did delivery go?" message goes out a few days after dispatch. The provider
    // holds it until then — this application does not run a timer of its own.
    private static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly ISmsNotificationProvider _provider;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<ContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        ISmsNotificationProvider provider,
        IAppLogger<OrderNotificationService> logger)
    {
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _provider = provider;
        _logger = logger;
    }

    public async Task<IReadOnlyList<OrderNotification>> NotifyOrderPlacedAsync(Order order, CancellationToken ct = default)
    {
        var body = $"eShop: thanks! Your order #{order.Id} has been placed.";
        return await SendImmediateToOwnerNumbersAsync(order, NotificationKind.OrderPlaced, body, ct);
    }

    public async Task<IReadOnlyList<OrderNotification>> NotifyOrderDispatchedAsync(Order order, CancellationToken ct = default)
    {
        var created = new List<OrderNotification>();

        var dispatchBody = $"eShop: good news — your order #{order.Id} is on its way!";
        created.AddRange(await SendImmediateToOwnerNumbersAsync(order, NotificationKind.OrderDispatched, dispatchBody, ct));

        // Queue the delivery follow-up with the provider for a few days later — one per number on file.
        var numbers = await GetOwnerNumbersAsync(order.BuyerId, ct);
        var sendAt = DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay);
        var followUpBody = $"eShop: how did the delivery of your order #{order.Id} go? Reply with your feedback — thank you!";

        foreach (var number in numbers)
        {
            var notification = new OrderNotification(order.BuyerId, order.Id, NotificationKind.DeliveryFollowUp,
                number, followUpBody, isScheduled: true, scheduledSendAt: sendAt);
            try
            {
                var result = await _provider.ScheduleAsync(number, followUpBody, sendAt, ct);
                notification.RecordSent(result.ProviderMessageSid, result.Status);
            }
            catch (Exception ex)
            {
                notification.RecordSendFailure(DescribeFailure(ex));
                _logger.LogWarning("Failed to schedule delivery follow-up for order {0}: {1}", order.Id, DescribeFailure(ex));
            }

            await _notifications.AddAsync(notification, ct);
            created.Add(notification);
        }

        return created;
    }

    public async Task<IReadOnlyList<OrderNotification>> NotifyOrderCancelledAsync(Order order, CancellationToken ct = default)
    {
        // Call off any not-yet-sent delivery follow-up first: a cancelled order must never trigger a
        // "how did delivery go?" message.
        var pending = await _notifications.ListAsync(new PendingFollowUpsForOrderSpecification(order.Id), ct);
        foreach (var followUp in pending)
        {
            try
            {
                await _provider.CancelScheduledAsync(followUp.ProviderMessageSid!, ct);
                followUp.MarkScheduleCancelled();
                await _notifications.UpdateAsync(followUp, ct);
            }
            catch (Exception ex)
            {
                // Do not fail the cancellation of the order; surface for operators via logs (no number).
                _logger.LogWarning("Failed to cancel scheduled follow-up for order {0}: {1}", order.Id, DescribeFailure(ex));
            }
        }

        var body = $"eShop: your order #{order.Id} has been cancelled. If this is unexpected, please contact us.";
        return await SendImmediateToOwnerNumbersAsync(order, NotificationKind.OrderCancelled, body, ct);
    }

    public async Task<OrderNotification> ResendAsync(OrderNotification original, string idempotencyKey, CancellationToken ct = default)
    {
        // Idempotency: a repeat under the same key returns the earlier resend rather than sending again.
        var existing = await _notifications.FirstOrDefaultAsync(
            new OrderNotificationByIdempotencyKeySpecification(idempotencyKey), ct);
        if (existing != null)
        {
            return existing;
        }

        if (original.Body == null)
        {
            // The content has been disposed of — there is nothing left to re-send.
            throw new SmsProviderException("The message content has been disposed of and can no longer be re-sent.");
        }

        var resend = new OrderNotification(original.OwnerId, original.OrderId, original.Kind, original.ToNumber, original.Body);
        resend.SetResendOrigin(original.Id, idempotencyKey);
        try
        {
            var result = await _provider.SendAsync(original.ToNumber, original.Body, ct);
            resend.RecordSent(result.ProviderMessageSid, result.Status);
        }
        catch (Exception ex)
        {
            resend.RecordSendFailure(DescribeFailure(ex));
            _logger.LogWarning("Resend for order {0} could not be handed to the provider: {1}", original.OrderId, DescribeFailure(ex));
        }

        await _notifications.AddAsync(resend, ct);
        return resend;
    }

    public async Task DisposeContentAsync(OrderNotification notification, CancellationToken ct = default)
    {
        // Redact at the provider first, so we never report the content gone locally while it survives there.
        if (notification.ProviderMessageSid != null && !notification.ContentDisposed)
        {
            await _provider.RedactContentAsync(notification.ProviderMessageSid, ct);
        }

        notification.DisposeContent();
        await _notifications.UpdateAsync(notification, ct);
    }

    public async Task RefreshStatusesAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken ct = default)
    {
        foreach (var notification in notifications)
        {
            if (notification.ProviderMessageSid == null)
            {
                continue;
            }

            // Skip messages already in a terminal state — their outcome will not change.
            if (ProviderMessageStatus.IsDelivered(notification.ProviderStatus) ||
                ProviderMessageStatus.IsUndeliverable(notification.ProviderStatus) ||
                notification.ScheduleCancelled)
            {
                continue;
            }

            try
            {
                var state = await _provider.FetchStateAsync(notification.ProviderMessageSid, ct);
                notification.UpdateDeliveryStatus(state.Status, state.ErrorCode, state.ErrorMessage);
                await _notifications.UpdateAsync(notification, ct);
            }
            catch (Exception ex)
            {
                // Best effort: keep the outcome already on record.
                _logger.LogWarning("Could not refresh delivery status for a notification on order {0}: {1}",
                    notification.OrderId, DescribeFailure(ex));
            }
        }
    }

    private async Task<IReadOnlyList<OrderNotification>> SendImmediateToOwnerNumbersAsync(
        Order order, NotificationKind kind, string body, CancellationToken ct)
    {
        var numbers = await GetOwnerNumbersAsync(order.BuyerId, ct);
        var created = new List<OrderNotification>();

        foreach (var number in numbers)
        {
            var notification = new OrderNotification(order.BuyerId, order.Id, kind, number, body);
            try
            {
                var result = await _provider.SendAsync(number, body, ct);
                notification.RecordSent(result.ProviderMessageSid, result.Status);
            }
            catch (Exception ex)
            {
                notification.RecordSendFailure(DescribeFailure(ex));
                _logger.LogWarning("Failed to send {0} notification for order {1}: {2}", kind, order.Id, DescribeFailure(ex));
            }

            await _notifications.AddAsync(notification, ct);
            created.Add(notification);
        }

        return created;
    }

    private async Task<IReadOnlyList<string>> GetOwnerNumbersAsync(string ownerId, CancellationToken ct)
    {
        var contacts = await _contactNumbers.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), ct);
        return contacts.Select(c => c.PhoneNumber).ToList();
    }

    private static string DescribeFailure(Exception ex) =>
        ex is SmsProviderException ? ex.Message : "the message could not be sent to the provider";
}
