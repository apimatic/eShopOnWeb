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
    private readonly IRepository<Order> _orders;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<NotificationResendRecord> _resends;
    private readonly IReadRepository<ContactNumber> _contactNumbers;
    private readonly ISmsGateway _smsGateway;
    private readonly OrderNotificationPublisher _publisher;

    public NotificationOperatorService(
        IRepository<Order> orders,
        IRepository<OrderNotification> notifications,
        IRepository<NotificationResendRecord> resends,
        IReadRepository<ContactNumber> contactNumbers,
        ISmsGateway smsGateway,
        OrderNotificationPublisher publisher)
    {
        _orders = orders;
        _notifications = notifications;
        _resends = resends;
        _contactNumbers = contactNumbers;
        _smsGateway = smsGateway;
        _publisher = publisher;
    }

    public async Task<OrderFulfillmentResult> DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        order.MarkDispatched();
        await _orders.UpdateAsync(order, cancellationToken);

        await _publisher.NotifyAsync(order.Id, order.BuyerId, OrderNotificationKind.OrderDispatched, sendAt: null, cancellationToken);
        await _publisher.NotifyAsync(
            order.Id,
            order.BuyerId,
            OrderNotificationKind.DeliveryFollowUp,
            DateTimeOffset.UtcNow.Add(OrderNotificationPublisher.DeliveryFollowUpDelay),
            cancellationToken);

        return await FulfillmentResultAsync(order, cancellationToken);
    }

    public async Task<OrderFulfillmentResult> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        order.MarkCancelled();
        await _orders.UpdateAsync(order, cancellationToken);

        await _publisher.CancelScheduledFollowUpsAsync(order.Id, cancellationToken);
        await _publisher.NotifyAsync(order.Id, order.BuyerId, OrderNotificationKind.OrderCancelled, sendAt: null, cancellationToken);

        return await FulfillmentResultAsync(order, cancellationToken);
    }

    public async Task<ResendNotificationResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("An idempotency key is required.", nameof(idempotencyKey));
        }

        var existing = await _resends.FirstOrDefaultAsync(
            new NotificationResendBySourceAndKeySpecification(notificationId, idempotencyKey.Trim()),
            cancellationToken);
        if (existing is not null)
        {
            var previous = await _notifications.GetByIdAsync(existing.ResultNotificationId, cancellationToken);
            if (previous is not null)
            {
                await _publisher.RefreshOneAsync(previous, cancellationToken);
                return new ResendNotificationResult
                {
                    NotificationId = previous.Id,
                    Notification = NotificationMapper.ToView(previous),
                    Replayed = true
                };
            }
        }

        var source = await _notifications.GetByIdAsync(notificationId, cancellationToken)
                     ?? throw new NotificationNotFoundException();

        await _publisher.RefreshOneAsync(source, cancellationToken);

        var status = source.ProviderStatus?.ToLowerInvariant();
        if (status is "delivered" or "read")
        {
            throw new InvalidOperationException("The original message already reached the shopper.");
        }

        var stillOnFile = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndPhoneSpecification(source.BuyerId, source.DestinationPhoneNumber),
            cancellationToken);
        if (stillOnFile is null)
        {
            throw new InvalidOperationException("The destination is no longer on file and must not be messaged again.");
        }

        var body = source.ContentRedacted || string.IsNullOrEmpty(source.Body)
            ? NotificationMapper.BodyFor(source.Kind, source.OrderId)
            : source.Body;

        var resent = await _publisher.SendAndRecordAsync(
            source.OrderId,
            source.BuyerId,
            stillOnFile.Id,
            stillOnFile.PhoneNumber,
            source.Kind,
            body,
            sendAt: null,
            sourceNotificationId: source.Id,
            cancellationToken);

        await _resends.AddAsync(new NotificationResendRecord(source.Id, idempotencyKey.Trim(), resent.Id), cancellationToken);

        return new ResendNotificationResult
        {
            NotificationId = resent.Id,
            Notification = NotificationMapper.ToView(resent),
            Replayed = false
        };
    }

    public async Task DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken)
                           ?? throw new NotificationNotFoundException();

        if (!string.IsNullOrWhiteSpace(notification.ProviderMessageSid))
        {
            var redacted = await _smsGateway.RedactBodyAsync(notification.ProviderMessageSid, cancellationToken);
            notification.UpdateProviderStatus(redacted.Status, redacted.ErrorCode, redacted.ErrorMessage);
        }

        notification.MarkContentRedacted();
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new ArgumentException("The 'to' timestamp must be on or after 'from'.");
        }

        var fromNumber = _smsGateway.FromNumber;
        if (string.IsNullOrWhiteSpace(fromNumber))
        {
            throw new InvalidOperationException("Twilio:FromNumber is not configured.");
        }

        var providerMessages = await _smsGateway.ListSentFromAsync(fromNumber, from, to, cancellationToken);
        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrWhiteSpace(m.Sid))
            .GroupBy(m => m.Sid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var providerSids = providerBySid.Keys.ToArray();
        var matchedBySid = await _notifications.ListAsync(
            new OrderNotificationsByProviderSidsSpecification(providerSids), cancellationToken);
        var createdInRange = await _notifications.ListAsync(
            new OrderNotificationsCreatedInRangeSpecification(from, to), cancellationToken);

        var application = matchedBySid
            .Concat(createdInRange)
            .GroupBy(n => n.Id)
            .Select(g => g.First())
            .ToList();

        var applicationBySid = application
            .Where(n => !string.IsNullOrWhiteSpace(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var matched = new List<ReconciliationMatch>();
        var providerOnly = new List<ReconciliationProviderOnly>();
        var applicationOnly = new List<ReconciliationApplicationOnly>();

        foreach (var provider in providerBySid.Values)
        {
            if (applicationBySid.TryGetValue(provider.Sid!, out var local))
            {
                matched.Add(new ReconciliationMatch
                {
                    NotificationId = local.Id,
                    ProviderMessageSid = provider.Sid!,
                    ApplicationStatus = local.ProviderStatus,
                    ProviderStatus = provider.Status
                });
            }
            else
            {
                providerOnly.Add(new ReconciliationProviderOnly
                {
                    ProviderMessageSid = provider.Sid!,
                    ProviderStatus = provider.Status,
                    DateSent = provider.DateSent
                });
            }
        }

        foreach (var local in application)
        {
            if (string.IsNullOrWhiteSpace(local.ProviderMessageSid)
                || !providerBySid.ContainsKey(local.ProviderMessageSid))
            {
                applicationOnly.Add(new ReconciliationApplicationOnly
                {
                    NotificationId = local.Id,
                    ProviderMessageSid = local.ProviderMessageSid,
                    ApplicationStatus = local.ProviderStatus
                });
            }
        }

        return new ReconciliationReport
        {
            From = from,
            To = to,
            FromNumber = fromNumber,
            Matched = matched,
            ProviderOnly = providerOnly,
            ApplicationOnly = applicationOnly
        };
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        return await _orders.GetByIdAsync(orderId, cancellationToken) ?? throw new OrderNotFoundException();
    }

    private async Task<OrderFulfillmentResult> FulfillmentResultAsync(Order order, CancellationToken cancellationToken)
    {
        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderIdSpecification(order.Id), cancellationToken);
        await _publisher.RefreshAsync(notifications, cancellationToken);
        return new OrderFulfillmentResult
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Notifications = notifications.Select(NotificationMapper.ToView).ToList()
        };
    }
}
