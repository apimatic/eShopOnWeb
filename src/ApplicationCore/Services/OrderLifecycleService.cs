using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderLifecycleService : IOrderLifecycleService
{
    public static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _catalogItemRepository;
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IUriComposer _uriComposer;
    private readonly ITwilioMessageClient _messageClient;
    private readonly IAppLogger<OrderLifecycleService> _logger;

    public OrderLifecycleService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> catalogItemRepository,
        IRepository<ContactNumber> contactNumberRepository,
        IRepository<OrderNotification> notificationRepository,
        IUriComposer uriComposer,
        ITwilioMessageClient messageClient,
        IAppLogger<OrderLifecycleService> logger)
    {
        _orderRepository = orderRepository;
        _catalogItemRepository = catalogItemRepository;
        _contactNumberRepository = contactNumberRepository;
        _notificationRepository = notificationRepository;
        _uriComposer = uriComposer;
        _messageClient = messageClient;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<CatalogOrderItem> items,
        Address? shipToAddress,
        CancellationToken cancellationToken = default)
    {
        if (items == null || items.Count == 0)
        {
            throw new BadRequestException("At least one catalog item is required.");
        }

        foreach (var item in items)
        {
            if (item.Quantity <= 0)
            {
                throw new BadRequestException("Each item quantity must be greater than zero.");
            }
        }

        var catalogItemIds = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItemRepository.ListAsync(
            new CatalogItemsSpecification(catalogItemIds), cancellationToken);

        if (catalogItems.Count != catalogItemIds.Length)
        {
            throw new BadRequestException("One or more catalog items were not found.");
        }

        var catalogById = catalogItems.ToDictionary(c => c.Id);
        var orderItems = items.Select(item =>
        {
            var catalogItem = catalogById[item.CatalogItemId];
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, item.Quantity);
        }).ToList();

        var address = shipToAddress ?? new Address("123 Main Street", "Seattle", "WA", "USA", "98101");
        var order = new Order(buyerId, address, orderItems);
        await _orderRepository.AddAsync(order, cancellationToken);

        await NotifySafelyAsync(
            order,
            OrderNotificationKinds.Placed,
            $"Your eShop order #{order.Id} has been placed. Thank you for shopping with us.",
            sendAt: null,
            cancellationToken);

        return order;
    }

    public async Task DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        order.MarkDispatched();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        await NotifySafelyAsync(
            order,
            OrderNotificationKinds.Dispatched,
            $"Your eShop order #{order.Id} is on its way.",
            sendAt: null,
            cancellationToken);

        await NotifySafelyAsync(
            order,
            OrderNotificationKinds.DeliveryFollowUp,
            $"How did the delivery of eShop order #{order.Id} go? We would love to hear how it went.",
            sendAt: DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay),
            cancellationToken);
    }

    public async Task CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        await CancelScheduledFollowUpsAsync(order.Id, cancellationToken);

        await NotifySafelyAsync(
            order,
            OrderNotificationKinds.Cancelled,
            $"Your eShop order #{order.Id} has been cancelled.",
            sendAt: null,
            cancellationToken);
    }

    public async Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(
        int orderId,
        string callerId,
        bool isAdministrator,
        CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        if (!isAdministrator && order.BuyerId != callerId)
        {
            throw new ForbiddenException("You cannot view notifications for another shopper's order.");
        }

        var notifications = await _notificationRepository.ListAsync(
            new NotificationsByOrderIdSpecification(orderId), cancellationToken);
        await RefreshFromProviderAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<IReadOnlyList<OrderNotification>> GetNotificationsForOrdersAsync(
        IEnumerable<int> orderIds,
        CancellationToken cancellationToken = default)
    {
        var ids = orderIds.ToArray();
        if (ids.Length == 0)
        {
            return Array.Empty<OrderNotification>();
        }

        var notifications = await _notificationRepository.ListAsync(
            new NotificationsByOrderIdsSpecification(ids), cancellationToken);
        await RefreshFromProviderAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<OrderNotification> ResendAsync(
        int notificationId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new BadRequestException("An idempotency key is required.");
        }

        var existing = await _notificationRepository.FirstOrDefaultAsync(
            new NotificationByResendIdempotencySpecification(notificationId, idempotencyKey),
            cancellationToken);
        if (existing != null)
        {
            await RefreshFromProviderAsync(new[] { existing }, cancellationToken);
            return existing;
        }

        var original = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new EntityNotFoundException("Notification was not found.");

        if (original.ContentRedacted || string.IsNullOrWhiteSpace(original.Body))
        {
            throw new BadRequestException("This message cannot be re-sent because its content has been disposed.");
        }

        var destinations = await GetActiveDestinationsAsync(original.BuyerId, cancellationToken);
        if (!destinations.Contains(original.DestinationNumber))
        {
            throw new BadRequestException("The original destination is no longer on file for this shopper.");
        }

        var snapshot = await SendOrCaptureFailureAsync(
            original.DestinationNumber,
            original.Body,
            sendAt: null,
            cancellationToken);

        var resent = new OrderNotification(
            original.OrderId,
            original.BuyerId,
            original.Kind,
            original.DestinationNumber,
            original.Body,
            snapshot.Sid,
            snapshot.Status,
            snapshot.ErrorCode,
            snapshot.ErrorMessage,
            scheduledAt: null,
            sourceNotificationId: original.Id,
            idempotencyKey: idempotencyKey);

        return await _notificationRepository.AddAsync(resent, cancellationToken);
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new EntityNotFoundException("Notification was not found.");

        if (!string.IsNullOrWhiteSpace(notification.ProviderMessageSid))
        {
            var updated = await _messageClient.UpdateAsync(
                notification.ProviderMessageSid,
                body: string.Empty,
                status: null,
                cancellationToken);
            notification.ApplyProviderState(
                updated.Status,
                updated.Body,
                updated.ErrorCode,
                updated.ErrorMessage,
                updated.Sid);
        }

        notification.MarkContentRedacted();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new BadRequestException("The 'to' timestamp must be on or after 'from'.");
        }

        var fromNumber = _messageClient.ConfiguredFromNumber;
        var providerMessages = await _messageClient.ListSentFromAsync(fromNumber, from, to, cancellationToken);
        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrWhiteSpace(m.Sid))
            .GroupBy(m => m.Sid)
            .ToDictionary(g => g.Key, g => g.First());

        var localInRange = await _notificationRepository.ListAsync(
            new NotificationsInCreatedRangeSpecification(from, to), cancellationToken);

        if (providerBySid.Count > 0)
        {
            var extraLocal = await _notificationRepository.ListAsync(
                new NotificationsByProviderSidsSpecification(providerBySid.Keys), cancellationToken);
            foreach (var extra in extraLocal)
            {
                if (localInRange.All(existing => existing.Id != extra.Id))
                {
                    localInRange.Add(extra);
                }
            }
        }

        var localBySid = localInRange
            .Where(n => !string.IsNullOrWhiteSpace(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new List<ReconciliationItem>();
        var providerOnly = new List<ReconciliationItem>();
        var applicationOnly = new List<ReconciliationItem>();

        foreach (var provider in providerBySid.Values)
        {
            if (localBySid.TryGetValue(provider.Sid, out var local))
            {
                matched.Add(ToReconciliationItem(local, provider));
            }
            else
            {
                providerOnly.Add(new ReconciliationItem(
                    NotificationId: null,
                    ProviderMessageSid: provider.Sid,
                    ProviderStatus: provider.Status,
                    Kind: "ProviderMessage",
                    Body: provider.Body));
            }
        }

        foreach (var local in localInRange)
        {
            if (string.IsNullOrWhiteSpace(local.ProviderMessageSid)
                || !providerBySid.ContainsKey(local.ProviderMessageSid))
            {
                applicationOnly.Add(ToReconciliationItem(local, provider: null));
            }
        }

        return new ReconciliationReport(from, to, fromNumber, matched, providerOnly, applicationOnly);
    }

    private static ReconciliationItem ToReconciliationItem(OrderNotification local, TwilioMessageSnapshot? provider)
    {
        return new ReconciliationItem(
            local.Id,
            local.ProviderMessageSid,
            provider?.Status ?? local.ProviderStatus,
            local.Kind,
            local.ContentRedacted ? null : (provider?.Body ?? local.Body));
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order == null)
        {
            throw new EntityNotFoundException("Order was not found.");
        }

        return order;
    }

    private async Task NotifySafelyAsync(
        Order order,
        string kind,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var destinations = await GetActiveDestinationsAsync(order.BuyerId, cancellationToken);
            if (destinations.Count == 0)
            {
                return;
            }

            foreach (var destination in destinations)
            {
                var snapshot = await SendOrCaptureFailureAsync(destination, body, sendAt, cancellationToken);
                var notification = new OrderNotification(
                    order.Id,
                    order.BuyerId,
                    kind,
                    destination,
                    body,
                    snapshot.Sid,
                    snapshot.Status,
                    snapshot.ErrorCode,
                    snapshot.ErrorMessage,
                    sendAt);
                await _notificationRepository.AddAsync(notification, cancellationToken);
            }
        }
        catch (Exception)
        {
            _logger.LogWarning("Order {OrderId} notification of kind {Kind} did not complete.", order.Id, kind);
        }
    }

    private async Task<TwilioMessageSnapshot> SendOrCaptureFailureAsync(
        string destination,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _messageClient.CreateAsync(
                new TwilioCreateMessageRequest(destination, body, sendAt),
                cancellationToken);
        }
        catch (Exception)
        {
            _logger.LogWarning("Provider rejected or failed a message create for kind send-at {HasSchedule}.", sendAt.HasValue);
            return new TwilioMessageSnapshot(
                Sid: string.Empty,
                Status: "failed",
                Body: body,
                To: null,
                From: null,
                ErrorCode: null,
                ErrorMessage: "The messaging provider did not accept the message.",
                DateSent: null);
        }
    }

    private async Task<IReadOnlyList<string>> GetActiveDestinationsAsync(string buyerId, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumberRepository.ListAsync(
            new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return numbers.Select(n => n.CanonicalNumber).Distinct().ToList();
    }

    private async Task CancelScheduledFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        try
        {
            var followUps = await _notificationRepository.ListAsync(
                new ScheduledFollowUpsByOrderSpecification(orderId), cancellationToken);

            foreach (var followUp in followUps)
            {
                if (string.IsNullOrWhiteSpace(followUp.ProviderMessageSid))
                {
                    continue;
                }

                try
                {
                    var updated = await _messageClient.UpdateAsync(
                        followUp.ProviderMessageSid,
                        body: null,
                        status: "canceled",
                        cancellationToken);
                    followUp.ApplyProviderState(
                        updated.Status,
                        updated.Body,
                        updated.ErrorCode,
                        updated.ErrorMessage,
                        updated.Sid);
                    await _notificationRepository.UpdateAsync(followUp, cancellationToken);
                }
                catch (Exception)
                {
                    _logger.LogWarning(
                        "Could not cancel scheduled follow-up {NotificationId} for order {OrderId}.",
                        followUp.Id,
                        orderId);
                }
            }
        }
        catch (Exception)
        {
            _logger.LogWarning("Failed while cancelling scheduled follow-ups for order {OrderId}.", orderId);
        }
    }

    private async Task RefreshFromProviderAsync(
        IReadOnlyList<OrderNotification> notifications,
        CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrWhiteSpace(notification.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var snapshot = await _messageClient.FetchAsync(notification.ProviderMessageSid, cancellationToken);
                notification.ApplyProviderState(
                    snapshot.Status,
                    snapshot.Body,
                    snapshot.ErrorCode,
                    snapshot.ErrorMessage,
                    snapshot.Sid);
                await _notificationRepository.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception)
            {
                _logger.LogWarning("Could not refresh provider status for notification {NotificationId}.", notification.Id);
            }
        }
    }
}
