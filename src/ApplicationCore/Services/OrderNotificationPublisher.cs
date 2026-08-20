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

public class OrderNotificationPublisher
{
    public static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<OrderNotification> _notifications;
    private readonly IReadRepository<ContactNumber> _contactNumbers;
    private readonly ISmsGateway _smsGateway;
    private readonly IAppLogger<OrderNotificationPublisher> _logger;

    public OrderNotificationPublisher(
        IRepository<OrderNotification> notifications,
        IReadRepository<ContactNumber> contactNumbers,
        ISmsGateway smsGateway,
        IAppLogger<OrderNotificationPublisher> logger)
    {
        _notifications = notifications;
        _contactNumbers = contactNumbers;
        _smsGateway = smsGateway;
        _logger = logger;
    }

    public async Task NotifyAsync(int orderId, string buyerId, OrderNotificationKind kind, DateTimeOffset? sendAt = null, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ContactNumber> contacts;
        try
        {
            contacts = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        }
        catch (Exception)
        {
            _logger.LogWarning("Failed to load contact numbers for order {OrderId}.", orderId);
            return;
        }

        if (contacts.Count == 0)
        {
            return;
        }

        var body = NotificationMapper.BodyFor(kind, orderId);
        foreach (var contact in contacts)
        {
            await SendAndRecordAsync(orderId, buyerId, contact.Id, contact.PhoneNumber, kind, body, sendAt, sourceNotificationId: null, cancellationToken);
        }
    }

    public async Task CancelScheduledFollowUpsAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var followUps = await _notifications.ListAsync(new ScheduledFollowUpsByOrderSpecification(orderId), cancellationToken);
        foreach (var followUp in followUps)
        {
            if (string.IsNullOrWhiteSpace(followUp.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var latest = await _smsGateway.FetchAsync(followUp.ProviderMessageSid, cancellationToken);
                if (!string.Equals(latest.Status, "scheduled", StringComparison.OrdinalIgnoreCase))
                {
                    followUp.UpdateProviderStatus(latest.Status, latest.ErrorCode, latest.ErrorMessage);
                    await _notifications.UpdateAsync(followUp, cancellationToken);
                    continue;
                }

                var cancelled = await _smsGateway.CancelScheduledAsync(followUp.ProviderMessageSid, cancellationToken);
                followUp.UpdateProviderStatus(cancelled.Status, cancelled.ErrorCode, cancelled.ErrorMessage);
                await _notifications.UpdateAsync(followUp, cancellationToken);
                _logger.LogInformation("Cancelled scheduled follow-up {NotificationId} for order {OrderId}.", followUp.Id, orderId);
            }
            catch (Exception)
            {
                _logger.LogWarning("Failed to cancel scheduled follow-up {NotificationId} for order {OrderId}.", followUp.Id, orderId);
            }
        }
    }

    public async Task RefreshAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken = default)
    {
        foreach (var notification in notifications)
        {
            await RefreshOneAsync(notification, cancellationToken);
        }
    }

    public async Task RefreshOneAsync(OrderNotification notification, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(notification.ProviderMessageSid))
        {
            return;
        }

        try
        {
            var latest = await _smsGateway.FetchAsync(notification.ProviderMessageSid, cancellationToken);
            notification.UpdateProviderStatus(latest.Status, latest.ErrorCode, latest.ErrorMessage);
            if (latest.Body is not null && notification.ContentRedacted == false)
            {
                // Keep local body unless the provider has already redacted it.
                if (latest.Body.Length == 0)
                {
                    notification.MarkContentRedacted();
                }
            }
            await _notifications.UpdateAsync(notification, cancellationToken);
        }
        catch (Exception)
        {
            _logger.LogWarning("Failed to refresh provider status for notification {NotificationId}.", notification.Id);
        }
    }

    public async Task<OrderNotification> SendAndRecordAsync(
        int orderId,
        string buyerId,
        int? contactNumberId,
        string destinationPhoneNumber,
        OrderNotificationKind kind,
        string body,
        DateTimeOffset? sendAt,
        int? sourceNotificationId,
        CancellationToken cancellationToken)
    {
        var notification = new OrderNotification(
            orderId,
            buyerId,
            contactNumberId,
            destinationPhoneNumber,
            kind,
            body,
            sendAt,
            sourceNotificationId);

        notification = await _notifications.AddAsync(notification, cancellationToken);

        try
        {
            var result = await _smsGateway.SendAsync(new SendMessageRequest
            {
                To = destinationPhoneNumber,
                Body = body,
                SendAt = sendAt
            }, cancellationToken);

            notification.RecordProviderResult(result.Sid, result.Status, result.ErrorCode, result.ErrorMessage);
        }
        catch (Exception)
        {
            notification.RecordProviderResult(null, "failed", null, "The message could not be handed to the provider.");
            _logger.LogWarning("Failed to hand notification {NotificationId} for order {OrderId} to the provider.", notification.Id, orderId);
        }

        await _notifications.UpdateAsync(notification, cancellationToken);
        return notification;
    }
}
