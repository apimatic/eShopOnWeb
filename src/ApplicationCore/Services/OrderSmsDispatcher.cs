using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderSmsDispatcher
{
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly ITwilioMessagingClient _messaging;
    private readonly IAppLogger<OrderSmsDispatcher> _logger;

    public OrderSmsDispatcher(
        IRepository<ContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        ITwilioMessagingClient messaging,
        IAppLogger<OrderSmsDispatcher> logger)
    {
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _messaging = messaging;
        _logger = logger;
    }

    public async Task NotifyOrderAsync(
        Order order,
        NotificationKind kind,
        string body,
        DateTimeOffset? sendAt = null,
        CancellationToken cancellationToken = default)
    {
        var contacts = await _contactNumbers.ListAsync(
            new ContactNumbersByBuyerSpecification(order.BuyerId, activeOnly: true),
            cancellationToken);

        if (contacts.Count == 0)
        {
            _logger.LogInformation("Skipping SMS for order {OrderId}; shopper has no number on file.", order.Id);
            return;
        }

        foreach (var contact in contacts)
        {
            await SendToContactAsync(order, contact, kind, body, sendAt, parentNotificationId: null, cancellationToken);
        }
    }

    public async Task<OrderNotification> SendToContactAsync(
        Order order,
        ContactNumber contact,
        NotificationKind kind,
        string body,
        DateTimeOffset? sendAt,
        int? parentNotificationId,
        CancellationToken cancellationToken)
    {
        var notification = new OrderNotification(
            order.Id,
            order.BuyerId,
            contact.Id,
            kind,
            body,
            sendAt,
            parentNotificationId);

        notification = await _notifications.AddAsync(notification, cancellationToken);

        try
        {
            var result = await _messaging.CreateMessageAsync(
                new OutboundSmsRequest(
                    To: contact.CanonicalNumber,
                    Body: body,
                    SendAt: sendAt),
                cancellationToken);

            notification.RecordProviderState(
                result.Sid,
                result.Status,
                result.ErrorCode,
                result.ErrorMessage,
                result.DateSent);

            await _notifications.UpdateAsync(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "SMS send failed for order {OrderId} notification {NotificationId}: {Message}",
                order.Id,
                notification.Id,
                PiiSafeException.Redact(ex.Message));

            notification.MarkSendFailed(PiiSafeException.Redact(ex.Message));
            await _notifications.UpdateAsync(notification, cancellationToken);
        }

        return notification;
    }

    public async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var followUps = await _notifications.ListAsync(new PendingFollowUpByOrderSpecification(orderId), cancellationToken);

        foreach (var followUp in followUps.Where(f => f.IsPendingSend() && !string.IsNullOrEmpty(f.ProviderMessageSid)))
        {
            try
            {
                var result = await _messaging.CancelMessageAsync(followUp.ProviderMessageSid!, cancellationToken);
                followUp.RecordProviderState(
                    result.Sid,
                    result.Status,
                    result.ErrorCode,
                    result.ErrorMessage,
                    result.DateSent);
                await _notifications.UpdateAsync(followUp, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Failed to cancel scheduled follow-up {NotificationId} for order {OrderId}: {Message}",
                    followUp.Id,
                    orderId,
                    PiiSafeException.Redact(ex.Message));
            }
        }
    }

    public async Task CancelPendingForContactAsync(int contactNumberId, CancellationToken cancellationToken)
    {
        var related = await _notifications.ListAsync(new NotificationsByContactNumberSpecification(contactNumberId), cancellationToken);

        foreach (var notification in related.Where(n => n.IsPendingSend() && !string.IsNullOrEmpty(n.ProviderMessageSid)))
        {
            try
            {
                var result = await _messaging.CancelMessageAsync(notification.ProviderMessageSid!, cancellationToken);
                notification.RecordProviderState(
                    result.Sid,
                    result.Status,
                    result.ErrorCode,
                    result.ErrorMessage,
                    result.DateSent);
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Failed to cancel pending message {NotificationId} after contact removal: {Message}",
                    notification.Id,
                    PiiSafeException.Redact(ex.Message));
            }
        }
    }

    public async Task RefreshStatusesAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid) || notification.IsTerminalStatus())
            {
                continue;
            }

            try
            {
                var result = await _messaging.FetchMessageAsync(notification.ProviderMessageSid, cancellationToken);
                notification.RecordProviderState(
                    result.Sid,
                    result.Status,
                    result.ErrorCode,
                    result.ErrorMessage,
                    result.DateSent);

                if (notification.ContentRedacted)
                {
                    notification.RedactContent();
                }

                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Failed to refresh provider status for notification {NotificationId}: {Message}",
                    notification.Id,
                    PiiSafeException.Redact(ex.Message));
            }
        }
    }
}

internal static class PiiSafeException
{
    public static string Redact(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return message;
        }

        return System.Text.RegularExpressions.Regex.Replace(
            message,
            @"\+\d{6,15}",
            "[redacted]");
    }
}
