using System;
using System.Collections.Generic;
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

public class NotificationOperatorService : INotificationOperatorService
{
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<Order> _orders;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly ITwilioMessagingClient _messaging;
    private readonly OrderNotificationSender _sender;

    public NotificationOperatorService(
        IRepository<OrderNotification> notifications,
        IRepository<Order> orders,
        IRepository<ContactNumber> contactNumbers,
        ITwilioMessagingClient messaging,
        OrderNotificationSender sender)
    {
        _notifications = notifications;
        _orders = orders;
        _contactNumbers = contactNumbers;
        _messaging = messaging;
        _sender = sender;
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new InvalidRequestException("An idempotency key is required.");
        }

        var existingResend = await _notifications.FirstOrDefaultAsync(
            new OrderNotificationResendByKeySpecification(notificationId, idempotencyKey),
            cancellationToken);
        if (existingResend is not null)
        {
            return existingResend;
        }

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (original is null)
        {
            throw new NotFoundException("Notification was not found.");
        }

        if (!original.DidNotReachShopper)
        {
            throw new InvalidRequestException("This message already reached the shopper and will not be re-sent.");
        }

        if (original.ContactNumberId.HasValue)
        {
            var contact = await _contactNumbers.GetByIdAsync(original.ContactNumberId.Value, cancellationToken);
            if (contact is null || contact.IsDeleted)
            {
                throw new InvalidRequestException("The destination number is no longer on file and nothing will be sent to it.");
            }
        }

        var order = await _orders.GetByIdAsync(original.OrderId, cancellationToken)
            ?? throw new NotFoundException("Order was not found.");

        return await _sender.SendToDestinationAsync(
            order,
            original.Kind,
            original.DestinationNumber,
            original.ContactNumberId,
            original.ResolveResendBody(),
            parentNotificationId: original.Id,
            idempotencyKey: idempotencyKey,
            sendAt: null,
            cancellationToken);
    }

    public async Task DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            throw new NotFoundException("Notification was not found.");
        }

        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            var updated = await _messaging.UpdateAsync(notification.ProviderMessageSid, body: string.Empty, status: null, cancellationToken);
            notification.ApplyProviderState(updated.Status ?? notification.ProviderStatus, updated.ErrorCode, updated.ErrorMessage, updated.Body);
        }

        notification.MarkContentDisposed();
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new InvalidRequestException("The 'to' timestamp must be on or after 'from'.");
        }

        var fromNumber = _messaging.ConfiguredFromNumber;
        if (string.IsNullOrWhiteSpace(fromNumber))
        {
            throw new InvalidOperationException("Twilio FromNumber must be configured for reconciliation.");
        }

        var providerMessages = await _messaging.ListFromNumberAsync(fromNumber, from, to, cancellationToken);
        var applicationNotifications = await _notifications.ListAsync(new OrderNotificationsInRangeSpecification(from, to), cancellationToken);

        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid))
            .GroupBy(m => m.Sid, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var applicationBySid = applicationNotifications
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var matched = new List<ReconciliationMatch>();
        foreach (var pair in applicationBySid)
        {
            if (providerBySid.TryGetValue(pair.Key, out var provider))
            {
                matched.Add(new ReconciliationMatch(pair.Value, provider));
            }
        }

        var providerOnly = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid) && !applicationBySid.ContainsKey(m.Sid))
            .ToList();

        var applicationOnly = applicationNotifications
            .Where(n => string.IsNullOrEmpty(n.ProviderMessageSid)
                        || !providerBySid.ContainsKey(n.ProviderMessageSid!))
            .ToList();

        return new ReconciliationReport(from, to, fromNumber, matched, providerOnly, applicationOnly);
    }
}
