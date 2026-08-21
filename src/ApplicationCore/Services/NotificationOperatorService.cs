using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class NotificationOperatorService : INotificationOperatorService
{
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<Order> _orders;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly ISmsGateway _smsGateway;
    private readonly OrderFlowService _orderFlow;
    private readonly IAppLogger<NotificationOperatorService> _logger;

    public NotificationOperatorService(
        IRepository<OrderNotification> notifications,
        IRepository<Order> orders,
        IRepository<ContactNumber> contactNumbers,
        ISmsGateway smsGateway,
        OrderFlowService orderFlow,
        IAppLogger<NotificationOperatorService> logger)
    {
        _notifications = notifications;
        _orders = orders;
        _contactNumbers = contactNumbers;
        _smsGateway = smsGateway;
        _orderFlow = orderFlow;
        _logger = logger;
    }

    public async Task<OrderNotification> ResendAsync(
        int notificationId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("An idempotency key is required.", nameof(idempotencyKey));
        }

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (original is null)
        {
            throw new KeyNotFoundException("Notification was not found.");
        }

        var existing = await _notifications.FirstOrDefaultAsync(
            new ResendNotificationByKeySpecification(original.Id, idempotencyKey),
            cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        var order = await _orders.GetByIdAsync(original.OrderId, cancellationToken);
        if (order is null)
        {
            throw new KeyNotFoundException("Order was not found.");
        }

        if (original.Kind == OrderNotificationKind.DeliveryFollowUp && order.Status == OrderStatus.Cancelled)
        {
            throw new InvalidOperationException("A delivery follow-up cannot be resent for a cancelled order.");
        }

        if (!original.DidNotReachShopper())
        {
            throw new InvalidOperationException("Only messages that did not reach the shopper can be resent.");
        }

        var registered = await _contactNumbers.ListAsync(
            new ContactNumbersByBuyerSpecification(original.BuyerId),
            cancellationToken);
        if (!registered.Any(c => c.CanonicalNumber == original.DestinationNumber))
        {
            throw new InvalidOperationException("The destination number is no longer on file and cannot be messaged.");
        }

        var kind = original.Kind;
        var body = original.ContentRedacted || string.IsNullOrEmpty(original.Body)
            ? OrderNotificationTemplates.BodyFor(kind, order.Id)
            : original.Body;

        return await _orderFlow.TrySendAsync(
            order,
            kind,
            original.DestinationNumber,
            body,
            sendAt: null,
            originalNotificationId: original.Id,
            idempotencyKey: idempotencyKey,
            cancellationToken);
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            throw new KeyNotFoundException("Notification was not found.");
        }

        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            try
            {
                var updated = await _smsGateway.RedactBodyAsync(notification.ProviderMessageSid, cancellationToken);
                notification.ApplyProviderState(
                    updated.Sid,
                    updated.Status,
                    updated.ErrorCode,
                    updated.Body,
                    updated.DateSent,
                    contentRedacted: true);
            }
            catch (Exception)
            {
                _logger.LogWarning("Provider content disposal failed for a notification on order {OrderId}.", notification.OrderId);
                throw;
            }
        }

        notification.RedactContent();
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new ArgumentException("The end of the range must not precede the start.");
        }

        var providerMessages = await _smsGateway.ListFromNumberAsync(from, to, cancellationToken);
        var local = await _notifications.ListAsync(new NotificationsInDateRangeSpecification(from, to), cancellationToken);

        var localBySid = local
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();
        var seenSids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var message in providerMessages)
        {
            if (string.IsNullOrEmpty(message.Sid))
            {
                continue;
            }

            seenSids.Add(message.Sid);
            if (localBySid.TryGetValue(message.Sid, out var localNote))
            {
                matched.Add(new ReconciliationEntry(
                    localNote.Id,
                    message.Sid,
                    localNote.ProviderStatus,
                    message.Status,
                    localNote.ContentRedacted ? null : localNote.Body));
            }
            else
            {
                providerOnly.Add(new ReconciliationEntry(
                    null,
                    message.Sid,
                    null,
                    message.Status,
                    message.Body));
            }
        }

        var localOnly = local
            .Where(n => string.IsNullOrEmpty(n.ProviderMessageSid)
                        || !seenSids.Contains(n.ProviderMessageSid))
            .Select(n => new ReconciliationEntry(
                n.Id,
                n.ProviderMessageSid,
                n.ProviderStatus,
                null,
                n.ContentRedacted ? null : n.Body))
            .ToList();

        return new ReconciliationReport(from, to, _smsGateway.SendingNumber, matched, providerOnly, localOnly);
    }
}
