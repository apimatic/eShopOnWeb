using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities.BasketAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    // How far ahead the "how did the delivery go?" follow-up is queued with the provider.
    // Within the provider's 15-minute-to-35-day scheduling window.
    private const int FollowUpDelayDays = 3;

    // Delivery outcomes that will not change again, so there is no point re-fetching them.
    private static readonly HashSet<string> TerminalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "delivered", "undelivered", "failed", "canceled"
    };

    // Orders placed through the API reuse the existing order model, which requires a ship-to address.
    // No address is part of this feature's surface, so a placeholder is used (as the web checkout does).
    private static Address DefaultShippingAddress() =>
        new("123 Main St.", "Kent", "OH", "United States", "44240");

    private readonly IRepository<Order> _orders;
    private readonly IRepository<Basket> _baskets;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IReadRepository<ContactNumber> _contactNumbers;
    private readonly IOrderService _orderService;
    private readonly ITwilioMessagingClient _messaging;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orders,
        IRepository<Basket> baskets,
        IRepository<CatalogItem> catalogItems,
        IRepository<OrderNotification> notifications,
        IReadRepository<ContactNumber> contactNumbers,
        IOrderService orderService,
        ITwilioMessagingClient messaging,
        IAppLogger<OrderNotificationService> logger)
    {
        _orders = orders;
        _baskets = baskets;
        _catalogItems = catalogItems;
        _notifications = notifications;
        _contactNumbers = contactNumbers;
        _orderService = orderService;
        _messaging = messaging;
        _logger = logger;
    }

    public async Task<Result<Order>> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> lines, CancellationToken cancellationToken = default)
    {
        if (lines is null || lines.Count == 0)
        {
            return Result<Order>.Invalid(new ValidationError { Identifier = "items", ErrorMessage = "At least one order line is required." });
        }

        if (lines.Any(l => l.Quantity <= 0))
        {
            return Result<Order>.Invalid(new ValidationError { Identifier = "items", ErrorMessage = "Every quantity must be greater than zero." });
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        var missing = ids.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
        {
            return Result<Order>.Invalid(new ValidationError
            {
                Identifier = "items",
                ErrorMessage = $"Unknown catalog item id(s): {string.Join(", ", missing)}."
            });
        }

        // Reuse the existing order-creation path: build a basket, then let OrderService turn it into an Order.
        var basket = new Basket(buyerId);
        foreach (var line in lines)
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            basket.AddItem(line.CatalogItemId, catalogItem.Price, line.Quantity);
        }
        await _baskets.AddAsync(basket, cancellationToken);

        var order = await _orderService.CreateOrderAsync(basket.Id, DefaultShippingAddress());

        // The basket was only a vehicle for reusing the existing creation path; it is not needed afterwards.
        await _baskets.DeleteAsync(basket, cancellationToken);

        await NotifyAsync(order, NotificationKind.OrderPlaced,
            $"Your eShop order #{order.Id} has been placed. Thank you for shopping with us!",
            schedule: false, cancellationToken);

        return Result<Order>.Success(order);
    }

    public async Task<Result> DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            return Result.NotFound();
        }

        if (order.Status != OrderStatus.Placed)
        {
            return Result.Conflict($"Order {orderId} cannot be dispatched from status {order.Status}.");
        }

        order.MarkDispatched();
        await _orders.UpdateAsync(order, cancellationToken);

        await NotifyAsync(order, NotificationKind.OrderDispatched,
            $"Good news! Your eShop order #{order.Id} is on its way.",
            schedule: false, cancellationToken);

        // Queue the delivery-feedback follow-up with the provider for a few days later.
        await NotifyAsync(order, NotificationKind.DeliveryFeedback,
            $"How did the delivery of your eShop order #{order.Id} go? We'd love your feedback.",
            schedule: true, cancellationToken);

        return Result.Success();
    }

    public async Task<Result> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            return Result.NotFound();
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            return Result.Conflict($"Order {orderId} is already cancelled.");
        }

        // Call off any not-yet-sent follow-up first, so it can never reach the customer.
        await CancelPendingFollowUpsAsync(order, cancellationToken);

        order.MarkCancelled();
        await _orders.UpdateAsync(order, cancellationToken);

        await NotifyAsync(order, NotificationKind.OrderCancelled,
            $"Your eShop order #{order.Id} has been cancelled. If this is unexpected, please contact support.",
            schedule: false, cancellationToken);

        return Result.Success();
    }

    public async Task<IReadOnlyList<OrderWithNotifications>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var result = new List<OrderWithNotifications>();
        foreach (var order in orders.OrderByDescending(o => o.OrderDate))
        {
            var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderSpecification(order.Id), cancellationToken);
            await RefreshStatusesAsync(notifications, cancellationToken);
            result.Add(new OrderWithNotifications(order, notifications));
        }
        return result;
    }

    public async Task<Result<IReadOnlyList<OrderNotification>>> GetNotificationsForOrderAsync(int orderId, string buyerId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order is null || !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            // Do not reveal another shopper's order.
            return Result<IReadOnlyList<OrderNotification>>.NotFound();
        }

        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderSpecification(orderId), cancellationToken);
        await RefreshStatusesAsync(notifications, cancellationToken);
        return Result<IReadOnlyList<OrderNotification>>.Success(notifications);
    }

    public async Task<Result<OrderNotification>> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Result<OrderNotification>.Invalid(new ValidationError
            {
                Identifier = "idempotencyKey",
                ErrorMessage = "An idempotency key is required."
            });
        }

        // Same key as a previous resend => return that result, never a second message.
        var priorForKey = await _notifications.ListAsync(new OrderNotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (priorForKey.Count > 0)
        {
            return Result<OrderNotification>.Success(priorForKey[0]);
        }

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (original is null)
        {
            return Result<OrderNotification>.NotFound();
        }

        if (original.ContentRedacted || string.IsNullOrEmpty(original.Body))
        {
            return Result<OrderNotification>.Conflict("The message content has been disposed of and cannot be resent.");
        }

        // Consent: never send to a number the shopper has since removed.
        var stillRegistered = (await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(original.BuyerId), cancellationToken))
            .Any(c => c.PhoneNumber == original.ToNumber);
        if (!stillRegistered)
        {
            return Result<OrderNotification>.Conflict("The destination number is no longer registered for this shopper.");
        }

        OrderNotification resend;
        try
        {
            var message = await _messaging.SendAsync(original.ToNumber, original.Body!, cancellationToken);
            resend = new OrderNotification(original.OrderId, original.BuyerId, NotificationKind.Resend, original.ToNumber, original.Body,
                message.Sid, message.Status ?? "unknown", message.ErrorCode, isScheduled: false,
                idempotencyKey: idempotencyKey, resendOfNotificationId: original.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Resend of notification {NotificationId} failed to send: {Error}", original.Id, ex.Message);
            resend = new OrderNotification(original.OrderId, original.BuyerId, NotificationKind.Resend, original.ToNumber, original.Body,
                providerMessageSid: null, providerStatus: "send_failed", providerErrorCode: null, isScheduled: false,
                idempotencyKey: idempotencyKey, resendOfNotificationId: original.Id);
        }

        await _notifications.AddAsync(resend, cancellationToken);
        return Result<OrderNotification>.Success(resend);
    }

    public async Task<Result> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            return Result.NotFound();
        }

        if (notification.ProviderMessageSid is not null && !notification.ContentRedacted)
        {
            try
            {
                await _messaging.RedactContentAsync(notification.ProviderMessageSid, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to dispose content for notification {NotificationId} at the provider: {Error}", notificationId, ex.Message);
                return Result.Error("The message content could not be disposed of at the provider. Please try again.");
            }
        }

        notification.RedactContent();
        await _notifications.UpdateAsync(notification, cancellationToken);
        _logger.LogInformation("Disposed of content for notification {NotificationId}.", notificationId);
        return Result.Success();
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default)
    {
        // Ask the provider directly for the configured sending number's messages in the range.
        var providerMessages = await _messaging.ListSentFromConfiguredNumberAsync(fromUtc, toUtc, cancellationToken);

        // eShop's own record of what it sent in the range (excluding not-yet-sent / called-off follow-ups).
        var eShopNotifications = (await _notifications.ListAsync(new SentOrderNotificationsInRangeSpecification(fromUtc, toUtc), cancellationToken))
            .Where(n => n.ProviderMessageSid is not null
                        && !string.Equals(n.ProviderStatus, "scheduled", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(n.ProviderStatus, "canceled", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid))
            .GroupBy(m => m.Sid!)
            .ToDictionary(g => g.Key, g => g.First());
        var eShopBySid = eShopNotifications
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();
        var eShopOnly = new List<ReconciliationEntry>();

        foreach (var (sid, message) in providerBySid)
        {
            if (eShopBySid.TryGetValue(sid, out var notification))
            {
                matched.Add(EntryFor(message, notification));
            }
            else
            {
                providerOnly.Add(EntryFor(message, null));
            }
        }

        foreach (var (sid, notification) in eShopBySid)
        {
            if (!providerBySid.ContainsKey(sid))
            {
                eShopOnly.Add(EntryFor(null, notification));
            }
        }

        return new ReconciliationReport(
            fromUtc, toUtc, _messaging.FromNumber,
            providerBySid.Count, eShopBySid.Count,
            matched, providerOnly, eShopOnly);
    }

    // ----- helpers -----

    private async Task NotifyAsync(Order order, NotificationKind kind, string body, bool schedule, CancellationToken cancellationToken)
    {
        var destination = await ResolveDestinationAsync(order.BuyerId, cancellationToken);
        if (destination is null)
        {
            // A shopper with no number on file is simply not messaged.
            _logger.LogInformation("Order {OrderId}: no contact number on file; {Kind} notification not sent.", order.Id, kind);
            return;
        }

        try
        {
            var message = schedule
                ? await _messaging.ScheduleAsync(destination, body, DateTimeOffset.UtcNow.AddDays(FollowUpDelayDays), cancellationToken)
                : await _messaging.SendAsync(destination, body, cancellationToken);

            var notification = new OrderNotification(order.Id, order.BuyerId, kind, destination, body,
                message.Sid, message.Status ?? "unknown", message.ErrorCode, isScheduled: schedule);
            await _notifications.AddAsync(notification, cancellationToken);
            _logger.LogInformation("Order {OrderId}: recorded {Kind} notification {NotificationId} (provider status {Status}).",
                order.Id, kind, notification.Id, notification.ProviderStatus);
        }
        catch (Exception ex)
        {
            // A message that cannot be sent must never fail the underlying operation.
            _logger.LogWarning("Order {OrderId}: {Kind} notification could not be sent: {Error}", order.Id, kind, ex.Message);
            var failed = new OrderNotification(order.Id, order.BuyerId, kind, destination, body,
                providerMessageSid: null, providerStatus: "send_failed", providerErrorCode: null, isScheduled: schedule);
            await _notifications.AddAsync(failed, cancellationToken);
        }
    }

    private async Task CancelPendingFollowUpsAsync(Order order, CancellationToken cancellationToken)
    {
        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderSpecification(order.Id), cancellationToken);
        var pending = notifications.Where(n =>
            n.Kind == NotificationKind.DeliveryFeedback &&
            n.IsScheduled &&
            n.ProviderMessageSid is not null &&
            !TerminalStatuses.Contains(n.ProviderStatus));

        foreach (var notification in pending)
        {
            try
            {
                var message = await _messaging.CancelScheduledAsync(notification.ProviderMessageSid!, cancellationToken);
                notification.UpdateProviderState(message.Sid, message.Status ?? "canceled", message.ErrorCode);
                notification.MarkCanceled();
                await _notifications.UpdateAsync(notification, cancellationToken);
                _logger.LogInformation("Order {OrderId}: called off scheduled follow-up notification {NotificationId}.", order.Id, notification.Id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Order {OrderId}: failed to call off follow-up notification {NotificationId}: {Error}", order.Id, notification.Id, ex.Message);
            }
        }
    }

    private async Task RefreshStatusesAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (notification.ProviderMessageSid is null || TerminalStatuses.Contains(notification.ProviderStatus))
            {
                continue;
            }

            try
            {
                var message = await _messaging.FetchAsync(notification.ProviderMessageSid, cancellationToken);
                notification.UpdateProviderState(message.Sid, message.Status ?? notification.ProviderStatus, message.ErrorCode);
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to refresh provider status for notification {NotificationId}: {Error}", notification.Id, ex.Message);
            }
        }
    }

    private async Task<string?> ResolveDestinationAsync(string buyerId, CancellationToken cancellationToken)
    {
        // Reach the shopper on their most recently registered number.
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return numbers.FirstOrDefault()?.PhoneNumber;
    }

    private static ReconciliationEntry EntryFor(ProviderMessage? message, OrderNotification? notification)
    {
        var sid = message?.Sid ?? notification?.ProviderMessageSid;
        var status = message?.Status ?? notification?.ProviderStatus;
        var errorCode = message?.ErrorCode ?? notification?.ProviderErrorCode;
        var masked = Mask(message?.To ?? notification?.ToNumber);
        return new ReconciliationEntry(
            sid, status, errorCode,
            notification?.Id, notification?.OrderId,
            notification?.Kind.ToString(),
            masked, message?.DateSent);
    }

    private static string? Mask(string? number)
    {
        if (string.IsNullOrEmpty(number))
        {
            return null;
        }

        var digits = number.Length;
        if (digits <= 4)
        {
            return new string('•', digits);
        }

        return new string('•', digits - 4) + number.Substring(digits - 4);
    }
}
