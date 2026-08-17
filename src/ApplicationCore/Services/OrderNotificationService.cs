using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    // The follow-up asking how delivery went is queued with the provider a few days after dispatch.
    // Within the provider's allowed 15-minute-to-7-day scheduling window.
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly ITwilioMessagingClient _twilio;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IRepository<ContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        ITwilioMessagingClient twilio,
        IUriComposer uriComposer,
        IAppLogger<OrderNotificationService> logger)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _twilio = twilio;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> lines, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
            throw new ArgumentException("An order must contain at least one line.", nameof(lines));
        if (lines.Any(l => l.Quantity <= 0))
            throw new ArgumentException("Every order line must have a quantity of at least 1.", nameof(lines));

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        var missing = ids.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
            throw new ArgumentException($"Unknown catalog item id(s): {string.Join(", ", missing)}.", nameof(lines));

        var orderItems = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        // No shipping address is collected on this API surface; use a placeholder for the required
        // owned value object so the existing Order aggregate is reused unchanged.
        var shipToAddress = new Address("N/A", "N/A", "N/A", "N/A", "N/A");
        var order = new Order(buyerId, shipToAddress, orderItems);
        order = await _orders.AddAsync(order, cancellationToken);

        var toNumber = await GetActiveNumberAsync(buyerId, cancellationToken);
        if (toNumber is not null)
        {
            await SendAndRecordAsync(order, buyerId, NotificationKind.OrderPlaced,
                $"eShop: thanks! Your order #{order.Id} has been placed.", toNumber, cancellationToken: cancellationToken);
        }

        return order;
    }

    public Task<Order?> GetOrderAsync(int orderId, CancellationToken cancellationToken = default) =>
        _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);

    public async Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
    }

    public async Task<Order?> DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
            return null;

        // The order lifecycle transition succeeds independently of messaging.
        order.MarkDispatched();
        await _orders.UpdateAsync(order, cancellationToken);

        var toNumber = await GetActiveNumberAsync(order.BuyerId, cancellationToken);
        if (toNumber is not null)
        {
            await SendAndRecordAsync(order, order.BuyerId, NotificationKind.OrderDispatched,
                $"eShop: good news — your order #{order.Id} is on its way!", toNumber, cancellationToken: cancellationToken);

            // Queue the "how did the delivery go?" follow-up with the provider for a few days later.
            var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
            await SendAndRecordAsync(order, order.BuyerId, NotificationKind.DeliveryFollowUp,
                $"eShop: how did the delivery of your order #{order.Id} go? We'd love your feedback.",
                toNumber, sendAt: sendAt, cancellationToken: cancellationToken);
        }

        return order;
    }

    public async Task<Order?> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
            return null;

        order.Cancel();
        await _orders.UpdateAsync(order, cancellationToken);

        // Call off any not-yet-sent follow-up so asking "how did delivery go?" never reaches a
        // customer whose order was cancelled.
        var pendingFollowUps = await _notifications.ListAsync(new ScheduledFollowUpForOrderSpecification(order.Id), cancellationToken);
        foreach (var followUp in pendingFollowUps)
        {
            if (string.IsNullOrEmpty(followUp.ProviderMessageSid))
                continue;
            try
            {
                var resource = await _twilio.CancelMessageAsync(followUp.ProviderMessageSid, cancellationToken);
                followUp.UpdateDeliveryState(MapStatus(resource.Status), resource.Status, resource.ErrorCode, resource.ErrorMessage);
                await _notifications.UpdateAsync(followUp, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not cancel scheduled follow-up {NotificationId} for order {OrderId}: {Error}",
                    followUp.Id, order.Id, ex.Message);
            }
        }

        var toNumber = await GetActiveNumberAsync(order.BuyerId, cancellationToken);
        if (toNumber is not null)
        {
            await SendAndRecordAsync(order, order.BuyerId, NotificationKind.OrderCancelled,
                $"eShop: your order #{order.Id} has been cancelled.", toNumber, cancellationToken: cancellationToken);
        }

        return order;
    }

    public async Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderSpecification(orderId), cancellationToken);
        await RefreshFromProviderAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<IReadOnlyList<OrderNotification>> GetNotificationsForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var notifications = await _notifications.ListAsync(new OrderNotificationsByBuyerSpecification(buyerId), cancellationToken);
        await RefreshFromProviderAsync(notifications, cancellationToken);
        return notifications;
    }

    public Task<OrderNotification?> GetNotificationAsync(int notificationId, CancellationToken cancellationToken = default) =>
        _notifications.GetByIdAsync(notificationId, cancellationToken);

    public async Task<ResendOutcome> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        // Repeating under the same idempotency key must not send a second message.
        var priorForKey = await _notifications.FirstOrDefaultAsync(
            new OrderNotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (priorForKey is not null)
        {
            return ResendOutcome.Replayed(priorForKey);
        }

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (original is null)
        {
            return ResendOutcome.NotFoundResult();
        }

        var order = await _orders.GetByIdAsync(original.OrderId, cancellationToken);
        var body = original.Body ?? $"eShop: an update about your order #{original.OrderId}.";

        var resend = new OrderNotification(original.OrderId, original.BuyerId, NotificationKind.Resend,
            original.ToNumber, body, idempotencyKey: idempotencyKey, originalNotificationId: original.Id);

        await SendAndPersistAsync(resend, new SendMessageRequest(original.ToNumber, body), cancellationToken);
        _logger.LogInformation("Operator re-sent notification {OriginalId} as {NewId} for order {OrderId}.",
            original.Id, resend.Id, original.OrderId);
        return ResendOutcome.Sent(resend);
    }

    public async Task<ContentDisposalOutcome> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            return ContentDisposalOutcome.NotFoundResult();
        }

        // Redact the body at the provider so the text is no longer retrievable there, then clear our
        // stored copy. The record of the send and its outcome survives.
        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            var resource = await _twilio.RedactMessageBodyAsync(notification.ProviderMessageSid, cancellationToken);
            notification.UpdateDeliveryState(MapStatus(resource.Status), resource.Status, resource.ErrorCode, resource.ErrorMessage);
        }

        notification.DisposeContent();
        await _notifications.UpdateAsync(notification, cancellationToken);
        _logger.LogInformation("Disposed of content for notification {NotificationId}.", notificationId);
        return ContentDisposalOutcome.Disposed(notification);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var providerMessages = await _twilio.ListMessagesFromConfiguredSenderAsync(from, to, cancellationToken);
        var eShopRecords = await _notifications.ListAsync(new OrderNotificationsCreatedBetweenSpecification(from, to), cancellationToken);

        var eShopBySid = eShopRecords
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());
        var providerSids = new HashSet<string>(providerMessages.Select(m => m.Sid));

        var matched = new List<ReconciliationMatch>();
        var providerOnly = new List<ProviderMessageSummary>();
        foreach (var message in providerMessages)
        {
            if (eShopBySid.TryGetValue(message.Sid, out var record))
            {
                matched.Add(new ReconciliationMatch
                {
                    ProviderMessageSid = message.Sid,
                    ProviderStatus = message.Status,
                    NotificationId = record.Id,
                    OrderId = record.OrderId,
                    Kind = record.Kind,
                    EShopStatus = record.DeliveryStatus
                });
            }
            else
            {
                providerOnly.Add(new ProviderMessageSummary
                {
                    ProviderMessageSid = message.Sid,
                    ProviderStatus = message.Status,
                    DateSent = message.DateSent,
                    ErrorCode = message.ErrorCode
                });
            }
        }

        var eShopOnly = eShopRecords
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid) && !providerSids.Contains(n.ProviderMessageSid!))
            .Select(n => new EShopNotificationSummary
            {
                NotificationId = n.Id,
                ProviderMessageSid = n.ProviderMessageSid!,
                OrderId = n.OrderId,
                Kind = n.Kind,
                EShopStatus = n.DeliveryStatus,
                CreatedAt = n.CreatedAt
            })
            .ToList();

        return new ReconciliationReport
        {
            From = from,
            To = to,
            ProviderMessageCount = providerMessages.Count,
            EShopRecordCount = eShopBySid.Count,
            Matched = matched,
            ProviderOnly = providerOnly,
            EShopOnly = eShopOnly
        };
    }

    private async Task<string?> GetActiveNumberAsync(string buyerId, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        // Spec orders by most-recently registered first.
        return numbers.FirstOrDefault()?.PhoneNumber;
    }

    private async Task<OrderNotification> SendAndRecordAsync(Order order, string buyerId, NotificationKind kind,
        string body, string toNumber, DateTimeOffset? sendAt = null, CancellationToken cancellationToken = default)
    {
        var notification = new OrderNotification(order.Id, buyerId, kind, toNumber, body, scheduledFor: sendAt);
        await SendAndPersistAsync(notification, new SendMessageRequest(toNumber, body, sendAt), cancellationToken);
        return notification;
    }

    private async Task SendAndPersistAsync(OrderNotification notification, SendMessageRequest request, CancellationToken cancellationToken)
    {
        // A message that cannot be sent must never fail the underlying operation. We record the
        // failure against the notification and carry on.
        try
        {
            var resource = await _twilio.SendMessageAsync(request, cancellationToken);
            notification.RecordProviderResult(resource.Sid, MapStatus(resource.Status), resource.Status,
                resource.ErrorCode, resource.ErrorMessage);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not send a {Kind} notification for order {OrderId}: {Error}",
                notification.Kind, notification.OrderId, ex.Message);
            notification.RecordSendFailure(ex.Message);
        }

        await _notifications.AddAsync(notification, cancellationToken);
    }

    private async Task RefreshFromProviderAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid) || notification.ContentDisposed)
                continue;
            if (IsTerminal(notification.DeliveryStatus))
                continue;

            try
            {
                var resource = await _twilio.FetchMessageAsync(notification.ProviderMessageSid, cancellationToken);
                var newStatus = MapStatus(resource.Status);
                if (newStatus != notification.DeliveryStatus
                    || resource.ErrorCode != notification.ErrorCode
                    || resource.Status != notification.ProviderStatus)
                {
                    notification.UpdateDeliveryState(newStatus, resource.Status, resource.ErrorCode, resource.ErrorMessage);
                    await _notifications.UpdateAsync(notification, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not refresh provider state for notification {NotificationId}: {Error}",
                    notification.Id, ex.Message);
            }
        }
    }

    private static bool IsTerminal(NotificationDeliveryStatus status) =>
        status is NotificationDeliveryStatus.Delivered
            or NotificationDeliveryStatus.Failed
            or NotificationDeliveryStatus.Undelivered
            or NotificationDeliveryStatus.Canceled;

    private static NotificationDeliveryStatus MapStatus(string? providerStatus) => providerStatus?.ToLowerInvariant() switch
    {
        "queued" or "accepted" => NotificationDeliveryStatus.Queued,
        "scheduled" => NotificationDeliveryStatus.Scheduled,
        "sending" => NotificationDeliveryStatus.Sending,
        "sent" => NotificationDeliveryStatus.Sent,
        "delivered" or "read" => NotificationDeliveryStatus.Delivered,
        "undelivered" => NotificationDeliveryStatus.Undelivered,
        "failed" => NotificationDeliveryStatus.Failed,
        "canceled" => NotificationDeliveryStatus.Canceled,
        null or "" => NotificationDeliveryStatus.NotSent,
        _ => NotificationDeliveryStatus.Unknown
    };
}
