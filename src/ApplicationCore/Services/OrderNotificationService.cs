using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Places orders using the existing order model and messages the shopper as an order moves. Every
/// send is best-effort: a message that cannot go out never fails the order operation, and a shopper
/// with no number on file is simply not messaged. Destination numbers and message bodies are never logged.
/// </summary>
public class OrderNotificationService : IOrderNotificationService
{
    // A few days later — comfortably inside the provider's 15-minute-to-35-day scheduling window.
    private static readonly TimeSpan FeedbackDelay = TimeSpan.FromDays(3);

    // Provider delivery outcomes that will not change again, so there is no need to keep re-fetching them.
    private static readonly HashSet<string> TerminalProviderStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "delivered", "undelivered", "failed", "canceled", "received", "read"
    };

    private readonly IRepository<Order> _orderRepository;
    private readonly IReadRepository<CatalogItem> _catalogRepository;
    private readonly IRepository<Notification> _notificationRepository;
    private readonly IReadRepository<ContactNumber> _contactNumberRepository;
    private readonly IUriComposer _uriComposer;
    private readonly ISmsProvider _smsProvider;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orderRepository,
        IReadRepository<CatalogItem> catalogRepository,
        IRepository<Notification> notificationRepository,
        IReadRepository<ContactNumber> contactNumberRepository,
        IUriComposer uriComposer,
        ISmsProvider smsProvider,
        IAppLogger<OrderNotificationService> logger)
    {
        _orderRepository = orderRepository;
        _catalogRepository = catalogRepository;
        _notificationRepository = notificationRepository;
        _contactNumberRepository = contactNumberRepository;
        _uriComposer = uriComposer;
        _smsProvider = smsProvider;
        _logger = logger;
    }

    public async Task<PlaceOrderResult> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines, Address shipToAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(shipToAddress, nameof(shipToAddress));

        if (lines == null || lines.Count == 0)
        {
            return new PlaceOrderResult(null, "An order must contain at least one item.");
        }
        if (lines.Any(l => l.Units <= 0))
        {
            return new PlaceOrderResult(null, "Every item quantity must be greater than zero.");
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        var missing = ids.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
        {
            return new PlaceOrderResult(null, $"Unknown catalog item(s): {string.Join(", ", missing)}.");
        }

        var orderItems = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Units);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, orderItems);
        order = await _orderRepository.AddAsync(order, cancellationToken);

        var body = $"eShop: your order #{order.Id} has been placed ({orderItems.Count} item(s), total {FormatMoney(order.Total())}).";
        await NotifyAsync(order, NotificationKind.OrderPlaced, body, schedule: false, cancellationToken);

        return new PlaceOrderResult(order, null);
    }

    public async Task<Order?> DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order == null)
        {
            return null;
        }
        if (order.Status == OrderStatus.Dispatched)
        {
            return order; // Already dispatched — do not send duplicate messages or schedule a second follow-up.
        }

        order.MarkDispatched(); // Throws if the order was cancelled.
        await _orderRepository.UpdateAsync(order, cancellationToken);

        var dispatchedBody = $"eShop: good news — your order #{order.Id} is on its way!";
        await NotifyAsync(order, NotificationKind.OrderDispatched, dispatchedBody, schedule: false, cancellationToken);

        // Queue the "how did delivery go?" follow-up WITH THE PROVIDER for a few days later — not held here.
        var feedbackBody = $"eShop: how did the delivery of your order #{order.Id} go? We'd love your feedback.";
        await NotifyAsync(order, NotificationKind.DeliveryFeedback, feedbackBody, schedule: true, cancellationToken);

        return order;
    }

    public async Task<Order?> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order == null)
        {
            return null;
        }
        if (order.Status == OrderStatus.Cancelled)
        {
            return order; // Already cancelled.
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        // Call off any delivery-feedback follow-up still queued with the provider BEFORE it can go out.
        await CancelScheduledFeedbackAsync(order.Id, cancellationToken);

        var body = $"eShop: your order #{order.Id} has been cancelled. If this is unexpected, please contact support.";
        await NotifyAsync(order, NotificationKind.OrderCancelled, body, schedule: false, cancellationToken);

        return order;
    }

    public async Task<IReadOnlyList<OrderWithNotifications>> GetOrdersWithNotificationsForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        if (orders.Count == 0)
        {
            return Array.Empty<OrderWithNotifications>();
        }

        var orderIds = orders.Select(o => o.Id).ToList();
        var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderIdsSpecification(orderIds), cancellationToken);
        await RefreshStatusesAsync(notifications, cancellationToken);

        var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => (IReadOnlyList<Notification>)g.ToList());
        return orders
            .Select(o => new OrderWithNotifications(o, byOrder.TryGetValue(o.Id, out var ns) ? ns : Array.Empty<Notification>()))
            .ToList();
    }

    public async Task<IReadOnlyList<Notification>?> GetNotificationsForOwnerAsync(int orderId, string ownerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order == null || order.BuyerId != ownerId)
        {
            return null; // Not the caller's order — do not reveal it exists.
        }

        var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderSpecification(orderId), cancellationToken);
        await RefreshStatusesAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        // Idempotency: a repeat under the same key returns the earlier result and sends nothing further.
        var priorForKey = await _notificationRepository.FirstOrDefaultAsync(
            new NotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (priorForKey != null)
        {
            return new ResendResult(priorForKey, AlreadyProcessed: true, OriginalNotFound: false, Error: null);
        }

        var original = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (original == null)
        {
            return new ResendResult(null, false, OriginalNotFound: true, Error: null);
        }
        if (string.IsNullOrEmpty(original.Body))
        {
            return new ResendResult(null, false, false, "The message content has been disposed of and cannot be resent.");
        }

        var resend = new Notification(original.OwnerId, original.OrderId, original.Kind, original.ToPhoneNumber, original.Body);
        resend.MarkAsResendOf(original.Id, idempotencyKey);
        await TrySendAsync(resend, original.ToPhoneNumber, original.Body, cancellationToken);
        resend = await _notificationRepository.AddAsync(resend, cancellationToken);

        return new ResendResult(resend, false, false, null);
    }

    public async Task<bool> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (notification == null)
        {
            return false;
        }

        // Dispose of the body on the provider side too, so the text is no longer retrievable there.
        // A failure here is surfaced: this is not an order operation and the caller must know it did not happen.
        if (!string.IsNullOrEmpty(notification.ProviderMessageSid) && !notification.ContentRedacted)
        {
            await _smsProvider.RedactBodyAsync(notification.ProviderMessageSid!, cancellationToken);
        }

        notification.RedactContent();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
        _logger.LogInformation("Redacted content of notification {Id}.", notification.Id);
        return true;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Ask the provider for this application's own sending number's messages over the range (server-side).
        var providerMessages = await _smsProvider.ListSentMessagesAsync(from, to, cancellationToken);
        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid))
            .GroupBy(m => m.Sid)
            .ToDictionary(g => g.Key, g => g.First());

        var localNotifications = await _notificationRepository.ListAsync(new SentNotificationsInRangeSpecification(from, to), cancellationToken);
        var localBySid = localNotifications
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new List<ReconciliationMatch>();
        var eShopOnly = new List<ReconciliationEShopOnly>();
        foreach (var n in localBySid.Values)
        {
            if (providerBySid.TryGetValue(n.ProviderMessageSid!, out var pm))
            {
                matched.Add(new ReconciliationMatch(n.ProviderMessageSid!, n.Id, pm.Status, n.ProviderStatus));
            }
            else
            {
                eShopOnly.Add(new ReconciliationEShopOnly(n.ProviderMessageSid!, n.Id, n.ProviderStatus, n.SentAt));
            }
        }

        var providerOnly = providerBySid.Values
            .Where(pm => !localBySid.ContainsKey(pm.Sid))
            .Select(pm => new ReconciliationProviderOnly(pm.Sid, pm.Status, pm.DateSent))
            .ToList();

        return new ReconciliationReport(from, to, _smsProvider.SendingNumber, matched, providerOnly, eShopOnly);
    }

    // ---- helpers -------------------------------------------------------------------------------

    private async Task NotifyAsync(Order order, NotificationKind kind, string body, bool schedule, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumberRepository.ListAsync(new ContactNumbersByOwnerSpecification(order.BuyerId), cancellationToken);
        if (numbers.Count == 0)
        {
            // A shopper with no number on file is simply not messaged.
            _logger.LogInformation("Order {OrderId}: no contact number on file; shopper not messaged for {Kind}.", order.Id, kind);
            return;
        }

        foreach (var number in numbers)
        {
            var notification = new Notification(order.BuyerId, order.Id, kind, number.PhoneNumber, body);
            if (schedule)
            {
                await TryScheduleAsync(notification, number.PhoneNumber, body, cancellationToken);
            }
            else
            {
                await TrySendAsync(notification, number.PhoneNumber, body, cancellationToken);
            }
            await _notificationRepository.AddAsync(notification, cancellationToken);
        }
    }

    private async Task TrySendAsync(Notification notification, string toNumber, string body, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _smsProvider.SendAsync(toNumber, body, cancellationToken);
            notification.MarkSent(result.MessageSid, result.Status, result.ErrorCode);
        }
        catch (SmsProviderException ex)
        {
            // The order operation still succeeds; record the failed attempt without logging number/body.
            notification.MarkFailed(ex.ProviderStatus, ex.ProviderErrorCode);
            _logger.LogWarning("Notification for order {OrderId} could not be sent (provider code {Code}).", notification.OrderId, ex.ProviderErrorCode ?? 0);
        }
        catch (Exception)
        {
            notification.MarkFailed(null, null);
            _logger.LogWarning("Notification for order {OrderId} could not be sent (transport error).", notification.OrderId);
        }
    }

    private async Task TryScheduleAsync(Notification notification, string toNumber, string body, CancellationToken cancellationToken)
    {
        var sendAt = DateTimeOffset.UtcNow.Add(FeedbackDelay);
        try
        {
            var result = await _smsProvider.ScheduleAsync(toNumber, body, sendAt, cancellationToken);
            notification.MarkScheduled(result.MessageSid, result.Status, sendAt);
        }
        catch (SmsProviderException ex)
        {
            notification.MarkFailed(ex.ProviderStatus, ex.ProviderErrorCode);
            _logger.LogWarning("Follow-up for order {OrderId} could not be scheduled (provider code {Code}).", notification.OrderId, ex.ProviderErrorCode ?? 0);
        }
        catch (Exception)
        {
            notification.MarkFailed(null, null);
            _logger.LogWarning("Follow-up for order {OrderId} could not be scheduled (transport error).", notification.OrderId);
        }
    }

    private async Task CancelScheduledFeedbackAsync(int orderId, CancellationToken cancellationToken)
    {
        var scheduled = await _notificationRepository.ListAsync(new ScheduledFeedbackForOrderSpecification(orderId), cancellationToken);
        foreach (var notification in scheduled)
        {
            try
            {
                var status = await _smsProvider.CancelScheduledAsync(notification.ProviderMessageSid!, cancellationToken);
                notification.MarkCancelled(status.Status ?? "canceled");
                await _notificationRepository.UpdateAsync(notification, cancellationToken);
                _logger.LogInformation("Called off scheduled follow-up {Id} for order {OrderId}.", notification.Id, orderId);
            }
            catch (Exception)
            {
                // Never fail the cancel operation. Leave the local state as Scheduled so it reflects reality.
                _logger.LogWarning("Could not call off scheduled follow-up {Id} for order {OrderId} with the provider.", notification.Id, orderId);
            }
        }
    }

    private async Task RefreshStatusesAsync(IReadOnlyList<Notification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid))
            {
                continue;
            }
            if (notification.ProviderStatus != null && TerminalProviderStatuses.Contains(notification.ProviderStatus))
            {
                continue; // Outcome will not change again.
            }
            try
            {
                var status = await _smsProvider.FetchStatusAsync(notification.ProviderMessageSid!, cancellationToken);
                notification.UpdateProviderStatus(status.Status, status.ErrorCode);
                await _notificationRepository.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception)
            {
                // Best-effort refresh; keep the last-known outcome.
                _logger.LogWarning("Could not refresh provider status for notification {Id}.", notification.Id);
            }
        }
    }

    private static string FormatMoney(decimal amount) => "$" + amount.ToString("0.00", CultureInfo.InvariantCulture);
}
