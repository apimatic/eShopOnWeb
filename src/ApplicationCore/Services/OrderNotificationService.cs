using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    /// <summary>How far out the "how did delivery go?" follow-up is queued with the provider.</summary>
    private const int FollowUpDelayDays = 3;

    private static readonly Address PlaceholderShipTo =
        new("Not provided", "Not provided", "NA", "Not provided", "00000");

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IRepository<SmsNotification> _notificationRepository;
    private readonly ISmsProvider _smsProvider;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<ContactNumber> contactNumberRepository,
        IRepository<SmsNotification> notificationRepository,
        ISmsProvider smsProvider,
        IUriComposer uriComposer,
        IAppLogger<OrderNotificationService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _contactNumberRepository = contactNumberRepository;
        _notificationRepository = notificationRepository;
        _smsProvider = smsProvider;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    // ---- Place -----------------------------------------------------------------------------

    public async Task<PlaceOrderResult> PlaceOrderAsync(
        string buyerId, IReadOnlyList<OrderLineInput> lines, CancellationToken cancellationToken = default)
    {
        if (lines is null || lines.Count == 0)
            return PlaceOrderResult.Invalid("At least one order line is required.");
        if (lines.Any(l => l.Units <= 0))
            return PlaceOrderResult.Invalid("Every order line must have a quantity of at least 1.");

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        var missing = ids.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
            return PlaceOrderResult.Invalid($"Unknown catalog item id(s): {string.Join(", ", missing)}.");

        // Reuse the existing order / order-item model exactly as OrderService does.
        var items = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Units);
        }).ToList();

        var order = new Order(buyerId, PlaceholderShipTo, items);
        await _orderRepository.AddAsync(order, cancellationToken);

        await NotifyOrderEventAsync(order, SmsNotificationKind.OrderPlaced, scheduleFollowUp: false, cancellationToken);

        return PlaceOrderResult.Placed(order.Id);
    }

    // ---- Dispatch --------------------------------------------------------------------------

    public async Task<bool> DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
            return false;

        // "On its way" now, plus a follow-up queued with the provider for a few days later.
        await NotifyOrderEventAsync(order, SmsNotificationKind.OrderDispatched, scheduleFollowUp: true, cancellationToken);
        return true;
    }

    // ---- Cancel ----------------------------------------------------------------------------

    public async Task<bool> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
            return false;

        // Call off any not-yet-sent follow-up first so it can never reach the shopper, then notify.
        await CancelPendingFollowUpsAsync(orderId, cancellationToken);
        await NotifyOrderEventAsync(order, SmsNotificationKind.OrderCancelled, scheduleFollowUp: false, cancellationToken);
        return true;
    }

    // ---- Reads -----------------------------------------------------------------------------

    public async Task<IReadOnlyList<OrderView>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        if (orders.Count == 0)
            return Array.Empty<OrderView>();

        var orderIds = orders.Select(o => o.Id).ToArray();
        var notifications = await _notificationRepository.ListAsync(new SmsNotificationsByOrdersSpecification(orderIds), cancellationToken);
        await RefreshStatusesAsync(notifications, cancellationToken);

        var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => g.ToList());

        return orders
            .OrderByDescending(o => o.OrderDate)
            .Select(o => BuildOrderView(o, byOrder.TryGetValue(o.Id, out var list) ? list : new List<SmsNotification>()))
            .ToList();
    }

    public async Task<IReadOnlyList<NotificationView>?> GetOrderNotificationsAsync(
        int orderId, string? ownerIdOrNullForOperator, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
            return null;

        // A shopper may only see their own order's notifications; an operator may see any.
        if (ownerIdOrNullForOperator != null && !string.Equals(order.BuyerId, ownerIdOrNullForOperator, StringComparison.Ordinal))
            return null;

        var notifications = await _notificationRepository.ListAsync(new SmsNotificationsByOrderSpecification(orderId), cancellationToken);
        await RefreshStatusesAsync(notifications, cancellationToken);

        return notifications.Select(MapNotification).ToList();
    }

    // ---- internals -------------------------------------------------------------------------

    /// <summary>
    /// Sends the message(s) for one order event to every number the shopper has on file, recording a
    /// notification for each, and optionally queues a follow-up. Never throws: a messaging failure
    /// must not fail the order operation.
    /// </summary>
    private async Task NotifyOrderEventAsync(Order order, SmsNotificationKind kind, bool scheduleFollowUp, CancellationToken cancellationToken)
    {
        try
        {
            var numbers = await _contactNumberRepository.ListAsync(new ContactNumbersByOwnerSpecification(order.BuyerId), cancellationToken);
            if (numbers.Count == 0)
            {
                _logger.LogInformation("Order {OrderId}: no contact number on file; shopper not messaged for {Kind}.", order.Id, kind);
                return;
            }

            var body = SmsMessageTemplates.For(kind, order);

            foreach (var number in numbers)
            {
                await SendAndRecordAsync(order, kind, number.PhoneNumber, body, cancellationToken);

                if (scheduleFollowUp)
                    await ScheduleFollowUpAsync(order, number.PhoneNumber, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            // Belt-and-braces: the operation has already succeeded; notification is best-effort.
            _logger.LogWarning("Order {OrderId}: notifying {Kind} failed but the operation stands. {Error}", order.Id, kind, ex.Message);
        }
    }

    private async Task SendAndRecordAsync(Order order, SmsNotificationKind kind, string destination, string body, CancellationToken cancellationToken)
    {
        var notification = new SmsNotification(order.BuyerId, order.Id, kind, destination, body);
        try
        {
            var sent = await _smsProvider.SendAsync(destination, body, cancellationToken);
            notification.RecordSent(sent.Sid, sent.Status, sent.ErrorCode, sent.ErrorMessage, sent.DateSent);
        }
        catch (Exception ex)
        {
            notification.RecordSendFailure(ex.Message);
            _logger.LogWarning("Order {OrderId}: send of {Kind} failed. {Error}", order.Id, kind, ex.Message);
        }

        await _notificationRepository.AddAsync(notification, cancellationToken);
    }

    private async Task ScheduleFollowUpAsync(Order order, string destination, CancellationToken cancellationToken)
    {
        var body = SmsMessageTemplates.For(SmsNotificationKind.DeliveryFollowUp, order);
        var sendAt = DateTimeOffset.UtcNow.AddDays(FollowUpDelayDays);
        var notification = new SmsNotification(order.BuyerId, order.Id, SmsNotificationKind.DeliveryFollowUp, destination, body);
        try
        {
            var scheduled = await _smsProvider.ScheduleAsync(destination, body, sendAt, cancellationToken);
            notification.RecordScheduled(scheduled.Sid, scheduled.Status, sendAt);
        }
        catch (Exception ex)
        {
            notification.RecordSendFailure(ex.Message);
            _logger.LogWarning("Order {OrderId}: scheduling the delivery follow-up failed. {Error}", order.Id, ex.Message);
        }

        await _notificationRepository.AddAsync(notification, cancellationToken);
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        try
        {
            var notifications = await _notificationRepository.ListAsync(new SmsNotificationsByOrderSpecification(orderId), cancellationToken);
            var pendingFollowUps = notifications.Where(n =>
                n.Kind == SmsNotificationKind.DeliveryFollowUp &&
                n.IsScheduled &&
                n.ProviderMessageSid != null &&
                !n.Status.IsTerminal());

            foreach (var followUp in pendingFollowUps)
            {
                try
                {
                    var result = await _smsProvider.CancelScheduledAsync(followUp.ProviderMessageSid!, cancellationToken);
                    followUp.MarkCanceled(result.Status);
                }
                catch (Exception ex)
                {
                    // If it can no longer be cancelled, fetch the truth so we don't misreport it.
                    _logger.LogWarning("Order {OrderId}: cancelling a scheduled follow-up failed. {Error}", orderId, ex.Message);
                    try
                    {
                        var snapshot = await _smsProvider.FetchStatusAsync(followUp.ProviderMessageSid!, cancellationToken);
                        if (snapshot != null)
                            followUp.UpdateFromProvider(snapshot.Status, snapshot.ErrorCode, snapshot.ErrorMessage, snapshot.DateSent);
                    }
                    catch { /* best-effort */ }
                }

                await _notificationRepository.UpdateAsync(followUp, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Order {OrderId}: sweeping scheduled follow-ups failed. {Error}", orderId, ex.Message);
        }
    }

    private async Task RefreshStatusesAsync(IReadOnlyList<SmsNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var n in notifications)
        {
            if (n.ProviderMessageSid is null || n.Status.IsTerminal())
                continue;
            try
            {
                var snapshot = await _smsProvider.FetchStatusAsync(n.ProviderMessageSid, cancellationToken);
                if (snapshot != null)
                {
                    n.UpdateFromProvider(snapshot.Status, snapshot.ErrorCode, snapshot.ErrorMessage, snapshot.DateSent);
                    await _notificationRepository.UpdateAsync(n, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Refreshing status for notification {NotificationId} failed. {Error}", n.Id, ex.Message);
            }
        }
    }

    private OrderView BuildOrderView(Order order, IReadOnlyList<SmsNotification> notifications) =>
        new(
            OrderId: order.Id,
            OrderDate: order.OrderDate,
            Total: order.Total(),
            Items: order.OrderItems.Select(i => new OrderItemView(
                i.ItemOrdered.CatalogItemId, i.ItemOrdered.ProductName, i.UnitPrice, i.Units)).ToList(),
            Notifications: notifications.Select(MapNotification).ToList());

    private static NotificationView MapNotification(SmsNotification n) =>
        new(
            NotificationId: n.Id,
            OrderId: n.OrderId,
            Kind: n.Kind.ToString(),
            Status: n.Status.ToString(),
            ProviderStatus: n.ProviderStatus,
            ProviderMessageSid: n.ProviderMessageSid,
            ErrorCode: n.ErrorCode,
            ErrorMessage: n.ErrorMessage,
            ContentRedacted: n.ContentRedacted,
            ScheduledSendAt: n.ScheduledSendAt,
            SentAt: n.SentAt,
            CreatedDate: n.CreatedDate,
            Destination: PhoneMask.Mask(n.Destination));
}
