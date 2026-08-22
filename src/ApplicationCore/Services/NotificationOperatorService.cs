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
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class NotificationOperatorService : INotificationOperatorService
{
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<ResendIdempotencyRecord> _idempotency;
    private readonly IRepository<Order> _orders;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly ITwilioMessagingClient _messaging;
    private readonly OrderSmsDispatcher _smsDispatcher;
    private readonly IAppLogger<NotificationOperatorService> _logger;

    public NotificationOperatorService(
        IRepository<OrderNotification> notifications,
        IRepository<ResendIdempotencyRecord> idempotency,
        IRepository<Order> orders,
        IRepository<ContactNumber> contactNumbers,
        ITwilioMessagingClient messaging,
        OrderSmsDispatcher smsDispatcher,
        IAppLogger<NotificationOperatorService> logger)
    {
        _notifications = notifications;
        _idempotency = idempotency;
        _orders = orders;
        _contactNumbers = contactNumbers;
        _messaging = messaging;
        _smsDispatcher = smsDispatcher;
        _logger = logger;
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("An idempotency key is required.", nameof(idempotencyKey));
        }

        var existingKey = await _idempotency.FirstOrDefaultAsync(
            new ResendIdempotencySpecification(notificationId, idempotencyKey.Trim()),
            cancellationToken);

        if (existingKey is not null)
        {
            var previous = await _notifications.GetByIdAsync(existingKey.ResultNotificationId, cancellationToken);
            if (previous is not null)
            {
                return previous;
            }
        }

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new ResourceNotFoundException($"Notification {notificationId} was not found.");

        if (original.ContentRedacted)
        {
            throw new DomainConflictException("Cannot resend a notification whose content has been disposed.");
        }

        if (!original.DidNotReachShopper())
        {
            throw new DomainConflictException("Only messages that did not reach the shopper can be resent.");
        }

        var contact = await _contactNumbers.GetByIdAsync(original.ContactNumberId, cancellationToken);
        if (contact is null || !contact.IsActive)
        {
            throw new DomainConflictException("The destination number is no longer on file and cannot be messaged.");
        }

        var order = await _orders.GetByIdAsync(original.OrderId, cancellationToken)
            ?? throw new ResourceNotFoundException($"Order {original.OrderId} was not found.");

        var body = original.Body
            ?? $"Update for your eShopOnWeb order #{order.Id}.";

        var resent = await _smsDispatcher.SendToContactAsync(
            order,
            contact,
            NotificationKind.Resend,
            body,
            sendAt: null,
            parentNotificationId: original.Id,
            cancellationToken);

        await _idempotency.AddAsync(
            new ResendIdempotencyRecord(original.Id, idempotencyKey.Trim(), resent.Id),
            cancellationToken);

        return resent;
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new ResourceNotFoundException($"Notification {notificationId} was not found.");

        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            try
            {
                var result = await _messaging.RedactMessageBodyAsync(notification.ProviderMessageSid, cancellationToken);
                notification.RecordProviderState(
                    result.Sid,
                    result.Status,
                    result.ErrorCode,
                    result.ErrorMessage,
                    result.DateSent);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Provider content disposal failed for notification {NotificationId}: {Message}",
                    notification.Id,
                    PiiSafeException.Redact(ex.Message));
                throw;
            }
        }

        notification.RedactContent();
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new ArgumentException("The 'to' timestamp must be on or after 'from'.");
        }

        var fromNumber = _messaging.ConfiguredFromNumber;
        var providerMessages = await _messaging.ListMessagesFromNumberAsync(fromNumber, from, to, cancellationToken);
        var local = await _notifications.ListAsync(new OrderNotificationsWithProviderSidSpecification(), cancellationToken);

        var localBySid = local
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var providerBySid = providerMessages
            .GroupBy(m => m.Sid, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var matched = new List<ProviderMessageState>();
        var providerOnly = new List<ProviderMessageState>();

        foreach (var message in providerMessages)
        {
            if (localBySid.ContainsKey(message.Sid))
            {
                matched.Add(message);
            }
            else
            {
                providerOnly.Add(message);
            }
        }

        var eShopOnly = local
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid)
                && !providerBySid.ContainsKey(n.ProviderMessageSid!)
                && NotificationInRange(n, from, to))
            .ToList();

        return new ReconciliationReport(from, to, fromNumber, matched, providerOnly, eShopOnly);
    }

    private static bool NotificationInRange(OrderNotification notification, DateTimeOffset from, DateTimeOffset to)
    {
        var timestamp = notification.ProviderDateSent ?? notification.CreatedAt;
        return timestamp >= from && timestamp <= to;
    }
}
