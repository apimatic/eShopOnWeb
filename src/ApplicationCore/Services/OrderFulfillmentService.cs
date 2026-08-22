using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderFulfillmentService : IOrderFulfillmentService
{
    public static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private static readonly Address DefaultShipToAddress =
        new("123 Main St.", "Kent", "OH", "USA", "44240");

    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<ShopperContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IUriComposer _uriComposer;
    private readonly ITwilioMessagingClient _messagingClient;
    private readonly TwilioSettings _twilioSettings;
    private readonly IAppLogger<OrderFulfillmentService> _logger;

    public OrderFulfillmentService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IRepository<ShopperContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        IUriComposer uriComposer,
        ITwilioMessagingClient messagingClient,
        TwilioSettings twilioSettings,
        IAppLogger<OrderFulfillmentService> logger)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _uriComposer = uriComposer;
        _messagingClient = messagingClient;
        _twilioSettings = twilioSettings;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> items, CancellationToken cancellationToken = default)
    {
        if (items is null || items.Count == 0)
        {
            throw new OrderFulfillmentException("At least one catalog item is required.");
        }

        if (items.Any(i => i.Quantity <= 0))
        {
            throw new OrderFulfillmentException("Each item quantity must be greater than zero.");
        }

        var catalogItemIds = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(catalogItemIds), cancellationToken);
        if (catalogItems.Count != catalogItemIds.Length)
        {
            throw new OrderFulfillmentException("One or more catalog items were not found.");
        }

        var orderItems = items.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, DefaultShipToAddress, orderItems);
        await _orders.AddAsync(order, cancellationToken);

        await NotifyAsync(order, NotificationKind.OrderPlaced, scheduleFollowUp: false, cancellationToken);
        return order;
    }

    public async Task<Order> DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await RequireOrderAsync(orderId, cancellationToken);
        try
        {
            order.MarkDispatched();
        }
        catch (InvalidOperationException ex)
        {
            throw new OrderFulfillmentException(ex.Message);
        }

        await _orders.UpdateAsync(order, cancellationToken);
        await NotifyAsync(order, NotificationKind.OrderDispatched, scheduleFollowUp: true, cancellationToken);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await RequireOrderAsync(orderId, cancellationToken);
        try
        {
            order.MarkCancelled();
        }
        catch (InvalidOperationException ex)
        {
            throw new OrderFulfillmentException(ex.Message);
        }

        await _orders.UpdateAsync(order, cancellationToken);
        await CancelOutstandingFollowUpsAsync(order.Id, cancellationToken);
        await NotifyAsync(order, NotificationKind.OrderCancelled, scheduleFollowUp: false, cancellationToken);
        return order;
    }

    public async Task<IReadOnlyList<Order>> ListBuyerOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        foreach (var order in orders)
        {
            var notifications = await _notifications.ListAsync(new NotificationsByOrderIdSpec(order.Id), cancellationToken);
            await SyncNotificationsAsync(notifications, cancellationToken);
        }

        return orders;
    }

    public async Task<IReadOnlyList<OrderNotification>> ListOrderNotificationsAsync(string buyerId, int orderId, CancellationToken cancellationToken = default)
    {
        var order = await RequireOrderAsync(orderId, cancellationToken);
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new KeyNotFoundException("Order was not found.");
        }

        var notifications = await _notifications.ListAsync(new NotificationsByOrderIdSpec(orderId), cancellationToken);
        await SyncNotificationsAsync(notifications, cancellationToken);
        return notifications;
    }

    private async Task<Order> RequireOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new KeyNotFoundException("Order was not found.");
        }

        return order;
    }

    private async Task NotifyAsync(Order order, NotificationKind kind, bool scheduleFollowUp, CancellationToken cancellationToken)
    {
        var contacts = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpec(order.BuyerId), cancellationToken);
        if (contacts.Count == 0)
        {
            _logger.LogInformation("Skipping {Kind} SMS for order {OrderId}; buyer {BuyerId} has no number on file.", kind, order.Id, order.BuyerId);
            return;
        }

        var body = OrderNotificationTemplates.For(kind, order.Id);
        foreach (var contact in contacts)
        {
            await SendAndRecordAsync(order, contact, kind, body, sendAt: null, sourceNotificationId: null, cancellationToken);
            if (scheduleFollowUp)
            {
                var followUpBody = OrderNotificationTemplates.For(NotificationKind.DeliveryFollowUp, order.Id);
                await SendAndRecordAsync(
                    order,
                    contact,
                    NotificationKind.DeliveryFollowUp,
                    followUpBody,
                    DateTimeOffset.UtcNow.Add(FollowUpDelay),
                    sourceNotificationId: null,
                    cancellationToken);
            }
        }
    }

    private async Task SendAndRecordAsync(
        Order order,
        ShopperContactNumber contact,
        NotificationKind kind,
        string body,
        DateTimeOffset? sendAt,
        int? sourceNotificationId,
        CancellationToken cancellationToken)
    {
        var notification = new OrderNotification(
            order.Id,
            order.BuyerId,
            contact.Id,
            contact.CanonicalNumber,
            kind,
            body,
            sendAt,
            sourceNotificationId);

        await _notifications.AddAsync(notification, cancellationToken);

        try
        {
            var request = new TwilioCreateMessageRequest
            {
                To = contact.CanonicalNumber,
                Body = body,
                From = _twilioSettings.FromNumber,
                MessagingServiceSid = _twilioSettings.MessagingServiceSid,
                ScheduleType = sendAt.HasValue ? "fixed" : null,
                SendAt = sendAt
            };

            var created = await _messagingClient.CreateMessageAsync(request, cancellationToken);
            if (string.IsNullOrEmpty(created.Sid) || string.IsNullOrEmpty(created.Status))
            {
                notification.RecordLocalSendFailure("The provider did not return a message identifier.");
            }
            else
            {
                notification.RecordProviderAccepted(created.Sid, created.Status, created.ErrorCode, created.ErrorMessage);
            }

            await _notifications.UpdateAsync(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Failed to send {Kind} notification {NotificationId} for order {OrderId}: {Message}",
                kind,
                notification.Id,
                order.Id,
                PiiRedactor.Redact(ex.Message));
            notification.RecordLocalSendFailure("The provider rejected or did not accept the message.");
            await _notifications.UpdateAsync(notification, cancellationToken);
        }
    }

    private async Task CancelOutstandingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var followUps = await _notifications.ListAsync(new ScheduledFollowUpsByOrderSpec(orderId), cancellationToken);
        foreach (var followUp in followUps)
        {
            if (string.IsNullOrEmpty(followUp.ProviderMessageSid))
            {
                followUp.ApplyProviderState("canceled", null, "Cancelled locally before the provider accepted the message.", followUp.Body);
                await _notifications.UpdateAsync(followUp, cancellationToken);
                continue;
            }

            try
            {
                var updated = await _messagingClient.UpdateMessageAsync(
                    followUp.ProviderMessageSid,
                    new TwilioUpdateMessageRequest { Status = "canceled" },
                    cancellationToken);
                followUp.ApplyProviderState(updated.Status ?? "canceled", updated.ErrorCode, updated.ErrorMessage, updated.Body);
                await _notifications.UpdateAsync(followUp, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Failed to cancel follow-up notification {NotificationId} for order {OrderId}: {Message}",
                    followUp.Id,
                    orderId,
                    PiiRedactor.Redact(ex.Message));
            }
        }
    }

    private async Task SyncNotificationsAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var snapshot = await _messagingClient.FetchMessageAsync(notification.ProviderMessageSid, cancellationToken);
                notification.ApplyProviderState(
                    snapshot.Status ?? notification.ProviderStatus,
                    snapshot.ErrorCode,
                    snapshot.ErrorMessage,
                    snapshot.Body);
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Failed to refresh notification {NotificationId} from the provider: {Message}",
                    notification.Id,
                    PiiRedactor.Redact(ex.Message));
            }
        }
    }
}
