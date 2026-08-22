using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    public static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private static readonly Address DefaultShippingAddress =
        new("123 Main Street", "Seattle", "WA", "United States", "98101");

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _catalogItemRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IRepository<NotificationResendRecord> _resendRepository;
    private readonly IContactNumberService _contactNumberService;
    private readonly ITwilioMessagingClient _messagingClient;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> catalogItemRepository,
        IRepository<OrderNotification> notificationRepository,
        IRepository<NotificationResendRecord> resendRepository,
        IContactNumberService contactNumberService,
        ITwilioMessagingClient messagingClient,
        IUriComposer uriComposer,
        IAppLogger<OrderNotificationService> logger)
    {
        _orderRepository = orderRepository;
        _catalogItemRepository = catalogItemRepository;
        _notificationRepository = notificationRepository;
        _resendRepository = resendRepository;
        _contactNumberService = contactNumberService;
        _messagingClient = messagingClient;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<CatalogOrderLine> items,
        ShippingAddressDto? shippingAddress,
        CancellationToken cancellationToken = default)
    {
        if (items == null || items.Count == 0)
        {
            throw new OrderNotificationException("At least one catalog item is required.");
        }

        var normalizedLines = items
            .GroupBy(i => i.CatalogItemId)
            .Select(g => new CatalogOrderLine(g.Key, g.Sum(x => x.Quantity)))
            .ToList();

        if (normalizedLines.Any(l => l.CatalogItemId <= 0 || l.Quantity <= 0))
        {
            throw new OrderNotificationException("Catalog item ids and quantities must be greater than zero.");
        }

        var catalogSpec = new CatalogItemsSpecification(normalizedLines.Select(l => l.CatalogItemId).ToArray());
        var catalogItems = await _catalogItemRepository.ListAsync(catalogSpec, cancellationToken);
        if (catalogItems.Count != normalizedLines.Count)
        {
            throw new OrderNotificationException("One or more catalog items were not found.");
        }

        var orderItems = normalizedLines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var pictureUri = _uriComposer.ComposePicUri(catalogItem.PictureUri);
            if (string.IsNullOrWhiteSpace(pictureUri))
            {
                pictureUri = "images/products/placeholder.png";
            }

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var address = shippingAddress == null
            ? DefaultShippingAddress
            : new Address(
                shippingAddress.Street,
                shippingAddress.City,
                shippingAddress.State,
                shippingAddress.Country,
                shippingAddress.ZipCode);

        var order = new Order(buyerId, address, orderItems);
        order = await _orderRepository.AddAsync(order, cancellationToken);

        await TrySendAsync(
            order,
            OrderNotificationKind.OrderPlaced,
            $"Your eShop order #{order.Id} has been placed.",
            sendAt: null,
            cancellationToken);

        return order;
    }

    public async Task<Order> DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        order.MarkDispatched();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        await TrySendAsync(
            order,
            OrderNotificationKind.OrderDispatched,
            $"Your eShop order #{order.Id} is on its way.",
            sendAt: null,
            cancellationToken);

        var sendAt = DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay);
        await TrySendAsync(
            order,
            OrderNotificationKind.DeliveryFollowUp,
            $"How did the delivery of your eShop order #{order.Id} go?",
            sendAt,
            cancellationToken);

        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        await CancelOutstandingFollowUpsAsync(order.Id, cancellationToken);

        await TrySendAsync(
            order,
            OrderNotificationKind.OrderCancelled,
            $"Your eShop order #{order.Id} has been cancelled.",
            sendAt: null,
            cancellationToken);

        return order;
    }

    public async Task<IReadOnlyList<OrderWithNotifications>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByBuyerSpecification(buyerId), cancellationToken);

        foreach (var notification in notifications)
        {
            await RefreshFromProviderAsync(notification, cancellationToken);
        }

        return orders
            .OrderByDescending(o => o.Id)
            .Select(order => new OrderWithNotifications(
                order,
                notifications.Where(n => n.OrderId == order.Id).ToList()))
            .ToList();
    }

    public async Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(string buyerId, int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        if (order.BuyerId != buyerId)
        {
            throw new ResourceNotFoundException("Order not found.");
        }

        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByOrderIdSpecification(orderId), cancellationToken);
        foreach (var notification in notifications)
        {
            await RefreshFromProviderAsync(notification, cancellationToken);
        }

        return notifications;
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new OrderNotificationException("An idempotency key is required.");
        }

        var existing = await _resendRepository.FirstOrDefaultAsync(
            new NotificationResendByKeySpecification(notificationId, idempotencyKey.Trim()),
            cancellationToken);
        if (existing != null)
        {
            var previous = await _notificationRepository.GetByIdAsync(existing.ResultNotificationId, cancellationToken);
            if (previous != null)
            {
                await RefreshFromProviderAsync(previous, cancellationToken);
                return previous;
            }
        }

        var source = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new ResourceNotFoundException("Notification not found.");

        await RefreshFromProviderAsync(source, cancellationToken);

        if (!source.DidNotReachRecipient())
        {
            throw new OrderNotificationException("Only messages that did not reach the shopper can be re-sent.");
        }

        var order = await GetOrderAsync(source.OrderId, cancellationToken);
        var body = source.ContentRedacted || string.IsNullOrWhiteSpace(source.Body)
            ? BuildBody(source.Kind, order.Id)
            : source.Body!;

        var resent = await TrySendAsync(order, source.Kind, body, sendAt: null, cancellationToken, source.Id);
        if (resent == null)
        {
            throw new OrderNotificationException("The message could not be re-sent because the shopper has no number on file or the provider rejected the request.");
        }

        var record = new NotificationResendRecord(source.Id, idempotencyKey.Trim(), resent.Id);
        try
        {
            await _resendRepository.AddAsync(record, cancellationToken);
        }
        catch (Exception)
        {
            var raced = await _resendRepository.FirstOrDefaultAsync(
                new NotificationResendByKeySpecification(notificationId, idempotencyKey.Trim()),
                cancellationToken);
            if (raced != null)
            {
                var originalResult = await _notificationRepository.GetByIdAsync(raced.ResultNotificationId, cancellationToken);
                if (originalResult != null)
                {
                    return originalResult;
                }
            }

            throw;
        }

        return resent;
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new ResourceNotFoundException("Notification not found.");

        if (!string.IsNullOrWhiteSpace(notification.ProviderMessageSid))
        {
            try
            {
                var updated = await _messagingClient.RedactBodyAsync(notification.ProviderMessageSid, cancellationToken);
                notification.SyncFromProvider(updated.Status, updated.ErrorCode, updated.Body);
            }
            catch (Exception)
            {
                _logger.LogWarning("Failed to redact provider content for notification {NotificationId}.", notification.Id);
                throw new OrderNotificationException("The provider could not dispose of the message content.");
            }
        }

        notification.MarkContentRedacted();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
    }

    public async Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new OrderNotificationException("The 'to' timestamp must be on or after 'from'.");
        }

        var providerMessages = await _messagingClient.ListFromConfiguredSenderAsync(from, to, cancellationToken);
        var localNotifications = await _notificationRepository.ListAsync(new OrderNotificationsInRangeSpecification(from, to), cancellationToken);

        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrWhiteSpace(m.Sid))
            .GroupBy(m => m.Sid, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var localBySid = localNotifications
            .Where(n => !string.IsNullOrWhiteSpace(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();
        var localOnly = new List<ReconciliationEntry>();

        foreach (var pair in providerBySid)
        {
            if (localBySid.TryGetValue(pair.Key, out var local))
            {
                matched.Add(new ReconciliationEntry(pair.Key, local.Id, local.ProviderStatus, pair.Value.Status, pair.Value.DateCreated ?? local.CreatedAt));
            }
            else
            {
                providerOnly.Add(new ReconciliationEntry(pair.Key, null, null, pair.Value.Status, pair.Value.DateCreated ?? pair.Value.DateSent));
            }
        }

        foreach (var local in localNotifications)
        {
            if (string.IsNullOrWhiteSpace(local.ProviderMessageSid) || !providerBySid.ContainsKey(local.ProviderMessageSid))
            {
                localOnly.Add(new ReconciliationEntry(local.ProviderMessageSid, local.Id, local.ProviderStatus, null, local.CreatedAt));
            }
        }

        return new NotificationReconciliationReport(from, to, matched, providerOnly, localOnly);
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order == null)
        {
            throw new ResourceNotFoundException("Order not found.");
        }

        return order;
    }

    private static string BuildBody(OrderNotificationKind kind, int orderId) => kind switch
    {
        OrderNotificationKind.OrderPlaced => $"Your eShop order #{orderId} has been placed.",
        OrderNotificationKind.OrderDispatched => $"Your eShop order #{orderId} is on its way.",
        OrderNotificationKind.DeliveryFollowUp => $"How did the delivery of your eShop order #{orderId} go?",
        OrderNotificationKind.OrderCancelled => $"Your eShop order #{orderId} has been cancelled.",
        _ => $"An update on your eShop order #{orderId}."
    };

    private async Task<OrderNotification?> TrySendAsync(
        Order order,
        OrderNotificationKind kind,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken,
        int? sourceNotificationId = null)
    {
        var destination = await _contactNumberService.GetActiveForBuyerAsync(order.BuyerId, cancellationToken);
        if (destination == null)
        {
            _logger.LogInformation("Skipping {Kind} SMS for order {OrderId}; shopper has no number on file.", kind, order.Id);
            return null;
        }

        var notification = new OrderNotification(order.Id, order.BuyerId, kind, body, sourceNotificationId);
        notification = await _notificationRepository.AddAsync(notification, cancellationToken);

        try
        {
            var result = await _messagingClient.SendAsync(destination.PhoneNumber, body, sendAt, cancellationToken);
            notification.RecordProviderAcceptance(result.Sid, result.Status, sendAt);
            await _notificationRepository.UpdateAsync(notification, cancellationToken);
            _logger.LogInformation("Recorded {Kind} SMS for order {OrderId} as provider message {MessageSid} with status {Status}.",
                kind, order.Id, result.Sid, result.Status);
            return notification;
        }
        catch (Exception)
        {
            notification.RecordProviderFailure(null);
            await _notificationRepository.UpdateAsync(notification, cancellationToken);
            _logger.LogWarning("Provider rejected or failed {Kind} SMS for order {OrderId}; the order operation still succeeded.",
                kind, order.Id);
            return notification;
        }
    }

    private async Task CancelOutstandingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var followUps = await _notificationRepository.ListAsync(new ScheduledFollowUpsForOrderSpecification(orderId), cancellationToken);
        foreach (var followUp in followUps)
        {
            await RefreshFromProviderAsync(followUp, cancellationToken);
            if (!followUp.IsNotYetSent() || string.IsNullOrWhiteSpace(followUp.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var updated = await _messagingClient.CancelAsync(followUp.ProviderMessageSid, cancellationToken);
                followUp.SyncFromProvider(updated.Status, updated.ErrorCode, updated.Body);
                await _notificationRepository.UpdateAsync(followUp, cancellationToken);
                _logger.LogInformation("Cancelled scheduled follow-up {MessageSid} for order {OrderId}.", followUp.ProviderMessageSid, orderId);
            }
            catch (Exception)
            {
                _logger.LogWarning("Failed to cancel scheduled follow-up for order {OrderId} notification {NotificationId}.", orderId, followUp.Id);
            }
        }
    }

    private async Task RefreshFromProviderAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(notification.ProviderMessageSid))
        {
            return;
        }

        try
        {
            var current = await _messagingClient.FetchAsync(notification.ProviderMessageSid, cancellationToken);
            var body = notification.ContentRedacted ? null : current.Body;
            notification.SyncFromProvider(current.Status, current.ErrorCode, body);
            if (notification.ContentRedacted)
            {
                notification.MarkContentRedacted();
            }
            await _notificationRepository.UpdateAsync(notification, cancellationToken);
        }
        catch (Exception)
        {
            _logger.LogWarning("Could not refresh provider status for notification {NotificationId}.", notification.Id);
        }
    }
}
