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
    // Delivery outcomes that no longer change — no need to re-read them from the provider.
    private static readonly HashSet<string> TerminalStatuses =
        new(StringComparer.OrdinalIgnoreCase) { "delivered", "undelivered", "failed", "canceled", "read" };

    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly ISmsGateway _smsGateway;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<ContactNumber> contactNumberRepository,
        IRepository<OrderNotification> notificationRepository,
        ISmsGateway smsGateway,
        IUriComposer uriComposer,
        IAppLogger<OrderNotificationService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _contactNumberRepository = contactNumberRepository;
        _notificationRepository = notificationRepository;
        _smsGateway = smsGateway;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, CancellationToken ct)
    {
        if (lines is null || lines.Count == 0)
        {
            throw new NotificationValidationException("An order must contain at least one item.");
        }
        if (lines.Any(l => l.Quantity <= 0))
        {
            throw new NotificationValidationException("Every order line must have a quantity of at least 1.");
        }

        var itemIds = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(itemIds), ct);

        var missing = itemIds.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
        {
            throw new NotificationValidationException(
                $"Unknown catalog item(s): {string.Join(", ", missing)}.");
        }

        var orderItems = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        // The task's order request carries only items; a ship-to address is not part of it, but the
        // reused Order aggregate requires one, so a placeholder is used — fulfilment is out of scope.
        var shipToAddress = new Address("Not provided", "Not provided", string.Empty, "Not provided", "00000");
        var order = new Order(buyerId, shipToAddress, orderItems);
        order = await _orderRepository.AddAsync(order, ct);

        _logger.LogInformation("Order {OrderId} placed for buyer with {ItemCount} line(s).",
            order.Id, orderItems.Count);

        await NotifyAsync(order, NotificationKind.OrderPlaced, BuildBody(NotificationKind.OrderPlaced, order.Id), ct);

        return order;
    }

    public async Task<bool> DispatchAsync(int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, ct);
        if (order is null)
        {
            return false;
        }

        _logger.LogInformation("Order {OrderId} dispatched.", order.Id);

        var numbers = await GetBuyerNumbersAsync(order.BuyerId, ct);
        foreach (var number in numbers)
        {
            // Tell the shopper it is on its way.
            await SendImmediateAsync(order, NotificationKind.Dispatched,
                BuildBody(NotificationKind.Dispatched, order.Id), number, ct);

            // Queue the "how did the delivery go?" follow-up WITH THE PROVIDER for a few days later.
            var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
            var followUp = new OrderNotification(order.Id, order.BuyerId, NotificationKind.DeliveryFollowUp,
                number, BuildBody(NotificationKind.DeliveryFollowUp, order.Id), scheduledFor: sendAt);
            await _notificationRepository.AddAsync(followUp, ct);

            var result = await _smsGateway.ScheduleAsync(number, followUp.Body!, sendAt, ct);
            followUp.RecordSendResult(result.ProviderMessageSid, result.Status, result.ErrorCode, result.ErrorMessage);
            await _notificationRepository.UpdateAsync(followUp, ct);
            LogSendOutcome(followUp);
        }

        return true;
    }

    public async Task<bool> CancelAsync(int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, ct);
        if (order is null)
        {
            return false;
        }

        // Call off any not-yet-sent follow-up FIRST — a "how did delivery go?" for a cancelled order
        // is exactly the incident this must prevent.
        var scheduled = await _notificationRepository.ListAsync(
            new ScheduledFollowUpsForOrderSpecification(orderId), ct);
        foreach (var followUp in scheduled)
        {
            try
            {
                await _smsGateway.CancelScheduledAsync(followUp.ProviderMessageSid!, ct);
                followUp.MarkCanceled();
                await _notificationRepository.UpdateAsync(followUp, ct);
                _logger.LogInformation("Scheduled follow-up {NotificationId} for order {OrderId} was called off.",
                    followUp.Id, orderId);
            }
            catch (SmsGatewayException ex)
            {
                _logger.LogWarning("Could not cancel scheduled follow-up {NotificationId} for order {OrderId}: {Status}",
                    followUp.Id, orderId, ex.StatusCode?.ToString() ?? "unknown");
            }
        }

        _logger.LogInformation("Order {OrderId} cancelled.", order.Id);

        var numbers = await GetBuyerNumbersAsync(order.BuyerId, ct);
        foreach (var number in numbers)
        {
            await SendImmediateAsync(order, NotificationKind.Cancelled,
                BuildBody(NotificationKind.Cancelled, order.Id), number, ct);
        }

        return true;
    }

    public async Task<OrderNotification?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new NotificationValidationException("An idempotency key is required to resend a message.");
        }

        // Same key ⇒ same request: return what the first attempt produced, send nothing.
        var existing = await _notificationRepository.FirstOrDefaultAsync(
            new OrderNotificationByIdempotencyKeySpecification(idempotencyKey), ct);
        if (existing is not null)
        {
            _logger.LogInformation(
                "Resend under idempotency key already produced notification {NotificationId}; not sending again.",
                existing.Id);
            return existing;
        }

        var source = await _notificationRepository.GetByIdAsync(notificationId, ct);
        if (source is null)
        {
            return null;
        }
        if (source.ContentDisposed || source.Body is null)
        {
            throw new NotificationValidationException(
                "Cannot resend a message whose content has been disposed of.");
        }

        // Record our own claim first (carrying the key), then call the provider, then settle it.
        var resend = new OrderNotification(source.OrderId, source.BuyerId, NotificationKind.Resend,
            source.ToPhoneNumber, source.Body, idempotencyKey: idempotencyKey,
            resendOfNotificationId: source.Id);
        await _notificationRepository.AddAsync(resend, ct);

        var result = await _smsGateway.SendAsync(resend.ToPhoneNumber, resend.Body!, ct);
        resend.RecordSendResult(result.ProviderMessageSid, result.Status, result.ErrorCode, result.ErrorMessage);
        await _notificationRepository.UpdateAsync(resend, ct);
        _logger.LogInformation("Resent notification {SourceId} as {NotificationId}.", source.Id, resend.Id);
        LogSendOutcome(resend);

        return resend;
    }

    public async Task<bool> DisposeContentAsync(int notificationId, CancellationToken ct)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, ct);
        if (notification is null || notification.ProviderMessageSid is null)
        {
            return false;
        }

        if (!notification.ContentDisposed)
        {
            // Redact at the provider first; only mark it disposed here once the provider confirms.
            await _smsGateway.DisposeContentAsync(notification.ProviderMessageSid, ct);
            notification.MarkContentDisposed();
            await _notificationRepository.UpdateAsync(notification, ct);
            _logger.LogInformation("Content of notification {NotificationId} was disposed of at the provider.",
                notification.Id);
        }

        return true;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var providerMessages = await _smsGateway.ListSentMessagesAsync(from, to, ct);
        var eShopNotifications = await _notificationRepository.ListAsync(
            new OrderNotificationsWithSidInRangeSpecification(from, to), ct);

        var eShopBySid = eShopNotifications
            .Where(n => n.ProviderMessageSid is not null)
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());
        var providerSids = new HashSet<string>(
            providerMessages.Where(m => m.Sid is not null).Select(m => m.Sid!));

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();

        foreach (var m in providerMessages)
        {
            if (m.Sid is not null && eShopBySid.TryGetValue(m.Sid, out var known))
            {
                matched.Add(new ReconciliationEntry(
                    m.Sid, m.Status, m.DateSent, known.Id, known.Status, known.Kind));
            }
            else
            {
                providerOnly.Add(new ReconciliationEntry(m.Sid, m.Status, m.DateSent, null, null, null));
            }
        }

        var eShopOnly = eShopNotifications
            .Where(n => n.ProviderMessageSid is not null && !providerSids.Contains(n.ProviderMessageSid!))
            .Select(n => new ReconciliationEntry(
                n.ProviderMessageSid, null, null, n.Id, n.Status, n.Kind))
            .ToList();

        _logger.LogInformation(
            "Reconciliation {From}..{To}: {Matched} matched, {ProviderOnly} provider-only, {EShopOnly} eShop-only.",
            from, to, matched.Count, providerOnly.Count, eShopOnly.Count);

        return new ReconciliationReport(
            from, to, _smsGateway.ConfiguredSendingNumber, matched, providerOnly, eShopOnly);
    }

    public async Task<IReadOnlyList<OrderWithNotifications>> GetMyOrdersAsync(string buyerId, CancellationToken ct)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);
        var notifications = await _notificationRepository.ListAsync(
            new OrderNotificationsByBuyerSpecification(buyerId), ct);

        await RefreshStatusesAsync(notifications, ct);

        var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => g.ToList());

        return orders
            .Select(o => new OrderWithNotifications(
                o,
                byOrder.TryGetValue(o.Id, out var list)
                    ? list
                    : (IReadOnlyList<OrderNotification>)Array.Empty<OrderNotification>()))
            .ToList();
    }

    public async Task<IReadOnlyList<OrderNotification>?> GetOrderNotificationsForBuyerAsync(
        int orderId, string buyerId, CancellationToken ct)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, ct);
        if (order is null || order.BuyerId != buyerId)
        {
            return null; // not the caller's order (or no such order) — do not reveal which
        }

        var notifications = await _notificationRepository.ListAsync(
            new OrderNotificationsByOrderSpecification(orderId), ct);
        await RefreshStatusesAsync(notifications, ct);
        return notifications;
    }

    // ----- helpers -----

    private async Task<IReadOnlyList<string>> GetBuyerNumbersAsync(string buyerId, CancellationToken ct)
    {
        var numbers = await _contactNumberRepository.ListAsync(
            new ContactNumbersByBuyerSpecification(buyerId), ct);
        if (numbers.Count == 0)
        {
            _logger.LogInformation("Buyer for the order has no contact number on file; not messaging.");
        }
        return numbers.Select(n => n.PhoneNumber).ToList();
    }

    private async Task NotifyAsync(Order order, NotificationKind kind, string body, CancellationToken ct)
    {
        var numbers = await GetBuyerNumbersAsync(order.BuyerId, ct);
        foreach (var number in numbers)
        {
            await SendImmediateAsync(order, kind, body, number, ct);
        }
    }

    private async Task SendImmediateAsync(
        Order order, NotificationKind kind, string body, string toNumber, CancellationToken ct)
    {
        var notification = new OrderNotification(order.Id, order.BuyerId, kind, toNumber, body);
        await _notificationRepository.AddAsync(notification, ct);

        var result = await _smsGateway.SendAsync(toNumber, body, ct);
        notification.RecordSendResult(result.ProviderMessageSid, result.Status, result.ErrorCode, result.ErrorMessage);
        await _notificationRepository.UpdateAsync(notification, ct);
        LogSendOutcome(notification);
    }

    private async Task RefreshStatusesAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken ct)
    {
        foreach (var n in notifications)
        {
            if (n.ProviderMessageSid is null || n.ContentDisposed)
            {
                continue;
            }
            if (n.Status is not null && TerminalStatuses.Contains(n.Status))
            {
                continue;
            }

            var status = await _smsGateway.GetStatusAsync(n.ProviderMessageSid, ct);
            if (status is not null)
            {
                n.UpdateStatus(status.Status, status.ErrorCode, status.ErrorMessage);
                await _notificationRepository.UpdateAsync(n, ct);
            }
        }
    }

    private void LogSendOutcome(OrderNotification n)
    {
        if (n.ProviderMessageSid is null)
        {
            _logger.LogWarning("Notification {NotificationId} ({Kind}) for order {OrderId} was not accepted: {Code}",
                n.Id, n.Kind, n.OrderId, n.ErrorCode?.ToString() ?? "no provider id");
        }
        else
        {
            _logger.LogInformation("Notification {NotificationId} ({Kind}) for order {OrderId}: sid {Sid}, status {Status}",
                n.Id, n.Kind, n.OrderId, n.ProviderMessageSid, n.Status ?? "unknown");
        }
    }

    private static string BuildBody(NotificationKind kind, int orderId) => kind switch
    {
        NotificationKind.OrderPlaced => $"eShop: thanks! Your order #{orderId} has been placed.",
        NotificationKind.Dispatched => $"eShop: good news — your order #{orderId} is on its way!",
        NotificationKind.Cancelled => $"eShop: your order #{orderId} has been cancelled.",
        NotificationKind.DeliveryFollowUp => $"eShop: how did the delivery of your order #{orderId} go? Reply to let us know.",
        _ => $"eShop: an update about your order #{orderId}."
    };
}
