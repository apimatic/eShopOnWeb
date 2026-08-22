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

public class OrderNotificationSender
{
    public static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly ITwilioMessageClient _twilio;
    private readonly IAppLogger<OrderNotificationSender> _logger;

    public OrderNotificationSender(
        IRepository<ContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        ITwilioMessageClient twilio,
        IAppLogger<OrderNotificationSender> logger)
    {
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _twilio = twilio;
        _logger = logger;
    }

    public async Task TrySendAsync(
        int orderId,
        string buyerId,
        string kind,
        string body,
        DateTimeOffset? sendAt = null,
        int? resentFromNotificationId = null,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        var destination = await GetActiveDestinationAsync(buyerId, cancellationToken);
        if (destination is null)
        {
            _logger.LogInformation("Skipping {Kind} notification for order {OrderId}; shopper has no number on file.", kind, orderId);
            return;
        }

        await SendToAsync(
            orderId,
            buyerId,
            destination,
            kind,
            body,
            sendAt,
            resentFromNotificationId,
            idempotencyKey,
            cancellationToken);
    }

    public async Task<OrderNotification?> TrySendResendAsync(
        OrderNotification original,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var destination = await ResolveResendDestinationAsync(original, cancellationToken);
        if (destination is null)
        {
            return null;
        }

        if (string.IsNullOrEmpty(original.Body) || original.ContentRedacted)
        {
            throw new InvalidOperationException("The original message content has been disposed and cannot be re-sent.");
        }

        return await SendToAsync(
            original.OrderId,
            original.BuyerId,
            destination,
            NotificationKind.Resend,
            original.Body,
            sendAt: null,
            original.Id,
            idempotencyKey,
            cancellationToken);
    }

    public async Task RefreshStatusesAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken = default)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var snapshot = await _twilio.FetchAsync(notification.ProviderMessageSid, cancellationToken);
                if (snapshot is null)
                {
                    continue;
                }

                notification.ApplyProviderStatus(snapshot.Status, snapshot.ErrorCode, snapshot.ErrorMessage);
                if (snapshot.Body is null || snapshot.Body.Length == 0)
                {
                    notification.RedactContent();
                }
            }
            catch (Exception)
            {
                _logger.LogWarning("Unable to refresh provider status for notification {NotificationId} (sid present).", notification.Id);
            }
        }

        foreach (var notification in notifications)
        {
            await _notifications.UpdateAsync(notification, cancellationToken);
        }
    }

    public async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var followUps = await _notifications.ListAsync(new ScheduledFollowUpsByOrderIdSpec(orderId), cancellationToken);
        foreach (var followUp in followUps)
        {
            if (string.IsNullOrEmpty(followUp.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var current = await _twilio.FetchAsync(followUp.ProviderMessageSid, cancellationToken);
                if (current is not null && !string.Equals(current.Status, "scheduled", StringComparison.OrdinalIgnoreCase))
                {
                    followUp.ApplyProviderStatus(current.Status, current.ErrorCode, current.ErrorMessage);
                    await _notifications.UpdateAsync(followUp, cancellationToken);
                    continue;
                }

                var cancelled = await _twilio.CancelScheduledAsync(followUp.ProviderMessageSid, cancellationToken);
                followUp.ApplyProviderStatus(cancelled.Status, cancelled.ErrorCode, cancelled.ErrorMessage);
                await _notifications.UpdateAsync(followUp, cancellationToken);
            }
            catch (Exception)
            {
                _logger.LogWarning("Unable to cancel scheduled follow-up notification {NotificationId} for order {OrderId}.", followUp.Id, orderId);
            }
        }
    }

    private async Task<OrderNotification> SendToAsync(
        int orderId,
        string buyerId,
        ContactNumber destination,
        string kind,
        string body,
        DateTimeOffset? sendAt,
        int? resentFromNotificationId,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var record = new OrderNotification(
            orderId,
            buyerId,
            destination.Id,
            destination.PhoneNumber,
            kind,
            body,
            sendAt,
            resentFromNotificationId,
            idempotencyKey);

        record = await _notifications.AddAsync(record, cancellationToken);

        try
        {
            var snapshot = await _twilio.SendAsync(destination.PhoneNumber, body, sendAt, cancellationToken);
            record.ApplyProviderResult(snapshot.Sid, snapshot.Status, snapshot.ErrorCode, snapshot.ErrorMessage);
        }
        catch (Exception)
        {
            record.MarkSendFailed("The provider did not accept the message.");
            _logger.LogWarning("Provider send failed for {Kind} notification {NotificationId} on order {OrderId}.", kind, record.Id, orderId);
        }

        await _notifications.UpdateAsync(record, cancellationToken);
        return record;
    }

    private async Task<ContactNumber?> GetActiveDestinationAsync(string buyerId, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerIdSpec(buyerId), cancellationToken);
        return numbers.FirstOrDefault();
    }

    private async Task<ContactNumber?> ResolveResendDestinationAsync(OrderNotification original, CancellationToken cancellationToken)
    {
        if (original.ContactNumberId is int contactNumberId)
        {
            var originalNumber = await _contactNumbers.FirstOrDefaultAsync(
                new ContactNumberByIdForBuyerSpec(contactNumberId, original.BuyerId),
                cancellationToken);
            if (originalNumber is not null)
            {
                return originalNumber;
            }
        }

        return await GetActiveDestinationAsync(original.BuyerId, cancellationToken);
    }
}
