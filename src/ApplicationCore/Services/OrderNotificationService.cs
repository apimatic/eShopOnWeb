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
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    /// <summary>How far ahead the "how did delivery go?" follow-up is queued with the provider.</summary>
    private static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private static readonly HashSet<string> TerminalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "delivered", "undelivered", "failed", NotificationStatuses.Canceled
    };

    private readonly IRepository<Order> _orderRepository;
    private readonly IReadRepository<CatalogItem> _itemRepository;
    private readonly IReadRepository<ContactNumber> _contactNumberRepository;
    private readonly IRepository<Notification> _notificationRepository;
    private readonly IUriComposer _uriComposer;
    private readonly ISmsGateway _smsGateway;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orderRepository,
        IReadRepository<CatalogItem> itemRepository,
        IReadRepository<ContactNumber> contactNumberRepository,
        IRepository<Notification> notificationRepository,
        IUriComposer uriComposer,
        ISmsGateway smsGateway,
        IAppLogger<OrderNotificationService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _contactNumberRepository = contactNumberRepository;
        _notificationRepository = notificationRepository;
        _uriComposer = uriComposer;
        _smsGateway = smsGateway;
        _logger = logger;
    }

    public async Task<PlaceOrderResult> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines, AddressData? shipToAddress, CancellationToken cancellationToken = default)
    {
        if (lines is null || lines.Count == 0)
            return PlaceOrderResult.Invalid("At least one order line is required.");
        if (lines.Any(l => l.Quantity <= 0))
            return PlaceOrderResult.Invalid("Every order line must have a quantity of at least 1.");

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        var catalogItemsById = catalogItems.ToDictionary(c => c.Id);

        var missing = ids.Where(id => !catalogItemsById.ContainsKey(id)).ToArray();
        if (missing.Length > 0)
            return PlaceOrderResult.Invalid($"Unknown catalog item id(s): {string.Join(", ", missing)}.");

        var items = lines.Select(line =>
        {
            var catalogItem = catalogItemsById[line.CatalogItemId];
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        // Shipping is not part of the notification feature; a placeholder satisfies the existing
        // Order invariant when the caller does not provide an address.
        var address = shipToAddress is null
            ? new Address("N/A", "N/A", "N/A", "N/A", "N/A")
            : new Address(shipToAddress.Street, shipToAddress.City, shipToAddress.State, shipToAddress.Country, shipToAddress.ZipCode);

        var order = new Order(buyerId, address, items);
        await _orderRepository.AddAsync(order, cancellationToken);

        await NotifyOrderPlacedAsync(order, cancellationToken);

        return PlaceOrderResult.Placed(order.Id);
    }

    public async Task<bool> DispatchOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
            return false;

        var numbers = await LoadBuyerNumbersAsync(order.BuyerId, cancellationToken);
        foreach (var number in numbers)
        {
            await SendAndRecordAsync(order.BuyerId, order.Id, NotificationType.OrderDispatched, number, DispatchedBody(order.Id), cancellationToken);
            await ScheduleFollowUpAsync(order.BuyerId, order.Id, number, cancellationToken);
        }

        return true;
    }

    public async Task<bool> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
            return false;

        var numbers = await LoadBuyerNumbersAsync(order.BuyerId, cancellationToken);
        foreach (var number in numbers)
        {
            await SendAndRecordAsync(order.BuyerId, order.Id, NotificationType.OrderCancelled, number, CancelledBody(order.Id), cancellationToken);
        }

        // Call off any follow-up that has not yet gone out so it never reaches the customer — asking
        // how a delivery went for a cancelled order is exactly the incident this prevents.
        await CancelPendingFollowUpsAsync(order.Id, cancellationToken);

        return true;
    }

    public async Task<IReadOnlyList<OrderNotificationsView>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var notifications = await _notificationRepository.ListAsync(new NotificationsByBuyerSpecification(buyerId), cancellationToken);
        var byOrder = notifications
            .GroupBy(n => n.OrderId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<NotificationView>)g.Select(NotificationView.From).ToList());

        return orders.Select(o => new OrderNotificationsView
        {
            OrderId = o.Id,
            OrderDate = o.OrderDate,
            Total = o.Total(),
            Notifications = byOrder.TryGetValue(o.Id, out var list) ? list : new List<NotificationView>()
        }).ToList();
    }

    public async Task<OrderNotificationsResult> GetOrderNotificationsAsync(int orderId, string buyerId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null || order.BuyerId != buyerId)
            return OrderNotificationsResult.NotFound();

        var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderSpecification(orderId), cancellationToken);

        // Refresh outcomes from the provider — there is no callback into this application, so the only
        // way to know what became of a message is to ask.
        foreach (var notification in notifications)
        {
            await RefreshStatusAsync(notification, cancellationToken);
        }

        return OrderNotificationsResult.Of(notifications.Select(NotificationView.From).ToList());
    }

    public async Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        // Idempotency: a repeat under the same key returns the message the first attempt produced —
        // no second message goes out.
        var existing = await _notificationRepository.FirstOrDefaultAsync(
            new NotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (existing is not null)
            return ResendResult.Success(existing);

        var original = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (original is null)
            return ResendResult.NotFound();
        if (original.ContentRedacted || string.IsNullOrEmpty(original.MessageBody))
            return ResendResult.ContentDisposed();

        var resend = new Notification(original.BuyerId, original.OrderId, original.Type, original.ToNumber, original.MessageBody!, idempotencyKey);
        try
        {
            var result = await _smsGateway.SendAsync(original.ToNumber, original.MessageBody!, cancellationToken);
            resend.MarkSent(result.Sid, result.Status);
        }
        catch (SmsGatewayException ex)
        {
            resend.MarkSendFailed();
            _logger.LogWarning("Resend of notification {0} could not be sent: {1}", original.Id, ex.Message);
        }

        await _notificationRepository.AddAsync(resend, cancellationToken);
        return ResendResult.Success(resend);
    }

    public async Task<DisposeContentOutcome> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
            return DisposeContentOutcome.NotFound;
        if (notification.ContentRedacted)
            return DisposeContentOutcome.Success; // already disposed — idempotent

        if (notification.ProviderMessageSid is not null)
        {
            try
            {
                await _smsGateway.RedactContentAsync(notification.ProviderMessageSid, cancellationToken);
            }
            catch (SmsGatewayException ex)
            {
                // Do not claim success — the text may still be retrievable at the provider.
                _logger.LogWarning("Provider content disposal failed for notification {0}: {1}", notification.Id, ex.Message);
                return DisposeContentOutcome.ProviderFailed;
            }
        }

        notification.RedactContent();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
        return DisposeContentOutcome.Success;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var providerMessages = await _smsGateway.ListSentMessagesAsync(from, to, cancellationToken);
        var localInRange = await _notificationRepository.ListAsync(new NotificationsSentInRangeSpecification(from, to), cancellationToken);

        var localBySid = localInRange
            .Where(n => n.ProviderMessageSid is not null)
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var providerSids = new HashSet<string>();
        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();

        foreach (var message in providerMessages)
        {
            if (message.Sid is not null)
                providerSids.Add(message.Sid);

            if (message.Sid is not null && localBySid.TryGetValue(message.Sid, out var local))
            {
                matched.Add(new ReconciliationEntry
                {
                    Sid = message.Sid,
                    ProviderStatus = message.Status,
                    NotificationId = local.Id,
                    OrderId = local.OrderId,
                    EShopStatus = local.Status
                });
            }
            else
            {
                providerOnly.Add(new ReconciliationEntry { Sid = message.Sid, ProviderStatus = message.Status });
            }
        }

        var eShopOnly = localInRange
            .Where(n => n.ProviderMessageSid is not null && !providerSids.Contains(n.ProviderMessageSid!))
            .Select(n => new ReconciliationEntry
            {
                Sid = n.ProviderMessageSid,
                NotificationId = n.Id,
                OrderId = n.OrderId,
                EShopStatus = n.Status
            })
            .ToList();

        return new ReconciliationReport
        {
            From = from,
            To = to,
            FromNumber = _smsGateway.SenderNumber,
            Matched = matched,
            ProviderOnly = providerOnly,
            EShopOnly = eShopOnly
        };
    }

    // ---- notification helpers -------------------------------------------------------------------

    private async Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken)
    {
        var numbers = await LoadBuyerNumbersAsync(order.BuyerId, cancellationToken);
        foreach (var number in numbers)
        {
            await SendAndRecordAsync(order.BuyerId, order.Id, NotificationType.OrderPlaced, number, PlacedBody(order.Id), cancellationToken);
        }
    }

    private async Task<IReadOnlyList<string>> LoadBuyerNumbersAsync(string buyerId, CancellationToken cancellationToken)
    {
        var contactNumbers = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return contactNumbers.Select(c => c.PhoneNumber).ToList();
    }

    /// <summary>
    /// Send a message and record the notification. A send that fails is recorded and never rethrown,
    /// so the underlying order operation still succeeds.
    /// </summary>
    private async Task SendAndRecordAsync(string buyerId, int orderId, NotificationType type, string toNumber, string body, CancellationToken cancellationToken)
    {
        var notification = new Notification(buyerId, orderId, type, toNumber, body);
        try
        {
            var result = await _smsGateway.SendAsync(toNumber, body, cancellationToken);
            notification.MarkSent(result.Sid, result.Status);
        }
        catch (SmsGatewayException ex)
        {
            notification.MarkSendFailed();
            _logger.LogWarning("SMS for order {0} ({1}) could not be sent: {2}", orderId, type, ex.Message);
        }

        await _notificationRepository.AddAsync(notification, cancellationToken);
    }

    private async Task ScheduleFollowUpAsync(string buyerId, int orderId, string toNumber, CancellationToken cancellationToken)
    {
        var sendAt = DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay);
        var body = FollowUpBody(orderId);
        var notification = new Notification(buyerId, orderId, NotificationType.DeliveryFollowUp, toNumber, body);
        try
        {
            var result = await _smsGateway.ScheduleAsync(toNumber, body, sendAt, cancellationToken);
            notification.MarkScheduled(result.Sid, result.Status, sendAt);
        }
        catch (SmsGatewayException ex)
        {
            notification.MarkSendFailed();
            _logger.LogWarning("Follow-up for order {0} could not be scheduled: {1}", orderId, ex.Message);
        }

        await _notificationRepository.AddAsync(notification, cancellationToken);
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var followUps = await _notificationRepository.ListAsync(new PendingFollowUpsByOrderSpecification(orderId), cancellationToken);
        foreach (var followUp in followUps)
        {
            try
            {
                await _smsGateway.CancelScheduledAsync(followUp.ProviderMessageSid!, cancellationToken);
                followUp.MarkCanceled();
            }
            catch (SmsGatewayException ex)
            {
                // The message may have already gone out or otherwise cannot be canceled; reflect the
                // provider's current view rather than asserting a cancellation that did not happen.
                _logger.LogWarning("Follow-up for order {0} could not be canceled: {1}", orderId, ex.Message);
                await RefreshStatusAsync(followUp, cancellationToken);
            }

            await _notificationRepository.UpdateAsync(followUp, cancellationToken);
        }
    }

    private async Task RefreshStatusAsync(Notification notification, CancellationToken cancellationToken)
    {
        if (notification.ProviderMessageSid is null || TerminalStatuses.Contains(notification.Status))
            return;

        try
        {
            var status = await _smsGateway.GetDeliveryStatusAsync(notification.ProviderMessageSid, cancellationToken);
            if (!string.IsNullOrWhiteSpace(status) && status != notification.Status)
            {
                notification.UpdateDeliveryStatus(status);
                await _notificationRepository.UpdateAsync(notification, cancellationToken);
            }
        }
        catch (SmsGatewayException ex)
        {
            _logger.LogWarning("Could not refresh status for notification {0}: {1}", notification.Id, ex.Message);
        }
    }

    // ---- message templates (order id only — no personal data) -----------------------------------

    private static string PlacedBody(int orderId) => $"eShop: your order #{orderId} has been placed. Thank you for shopping with us!";
    private static string DispatchedBody(int orderId) => $"eShop: good news — your order #{orderId} is on its way!";
    private static string CancelledBody(int orderId) => $"eShop: your order #{orderId} has been cancelled.";
    private static string FollowUpBody(int orderId) => $"eShop: how did the delivery of your order #{orderId} go? We'd love your feedback.";
}
