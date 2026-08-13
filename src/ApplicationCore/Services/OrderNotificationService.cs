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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Places orders from catalog items and drives the SMS notifications an order produces. A
/// messaging problem is recorded on the notification and never fails the order operation.
/// </summary>
public class OrderNotificationService : IOrderNotificationService
{
    // A "few days later" for the post-delivery follow-up.
    private const int FollowUpDelayDays = 3;

    // A placeholder ship-to address: this API places orders from catalog items only and the
    // existing Order model requires a (non-null) address. It is not part of the SMS flow.
    private static readonly Func<Address> PlaceholderAddress =
        () => new Address("N/A", "N/A", "N/A", "N/A", "00000");

    private readonly IRepository<Order> _orders;
    private readonly IReadRepository<CatalogItem> _catalogItems;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IReadRepository<ContactNumber> _contactNumbers;
    private readonly ISmsGateway _smsGateway;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orders,
        IReadRepository<CatalogItem> catalogItems,
        IRepository<OrderNotification> notifications,
        IReadRepository<ContactNumber> contactNumbers,
        ISmsGateway smsGateway,
        IUriComposer uriComposer,
        IAppLogger<OrderNotificationService> logger)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _notifications = notifications;
        _contactNumbers = contactNumbers;
        _smsGateway = smsGateway;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
        {
            throw new OrderRequestException("An order must contain at least one line item.");
        }
        if (lines.Any(l => l.Quantity <= 0))
        {
            throw new OrderRequestException("Every order line must have a quantity of at least 1.");
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        var byId = catalogItems.ToDictionary(c => c.Id);

        var missing = ids.Where(id => !byId.ContainsKey(id)).ToArray();
        if (missing.Length > 0)
        {
            throw new OrderRequestException($"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var orderItems = lines.Select(line =>
        {
            var catalogItem = byId[line.CatalogItemId];
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, PlaceholderAddress(), orderItems);
        await _orders.AddAsync(order, cancellationToken);
        _logger.LogInformation("Placed order {OrderId} for a shopper.", order.Id);

        await SendImmediateAsync(order, buyerId, NotificationKind.OrderPlaced, OrderPlacedBody(order), cancellationToken);
        return order;
    }

    public async Task DispatchOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            throw new EntityNotFoundException($"Order {orderId} was not found.");
        }

        // An invalid transition (e.g. dispatching a cancelled order) is a real failure of the
        // operation itself, and is allowed to surface.
        order.MarkDispatched();
        await _orders.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Dispatched order {OrderId}.", order.Id);

        await SendImmediateAsync(order, order.BuyerId, NotificationKind.OrderDispatched, DispatchedBody(order), cancellationToken);
        await ScheduleFollowUpAsync(order, cancellationToken);
    }

    public async Task CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            throw new EntityNotFoundException($"Order {orderId} was not found.");
        }

        order.MarkCancelled();
        await _orders.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Cancelled order {OrderId}.", order.Id);

        // Call off any follow-up that has not yet gone out BEFORE telling the shopper, so that a
        // "how did delivery go?" message can never reach them for a cancelled order.
        await CancelFollowUpsAsync(order.Id, cancellationToken);
        await SendImmediateAsync(order, order.BuyerId, NotificationKind.OrderCancelled, CancelledBody(order), cancellationToken);
    }

    public async Task<IReadOnlyList<OrderWithNotifications>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var result = new List<OrderWithNotifications>();
        foreach (var order in orders)
        {
            var notifications = await _notifications.ListAsync(new NotificationsByOrderSpecification(order.Id), cancellationToken);
            await RefreshDeliveryStatesAsync(notifications, cancellationToken);
            result.Add(new OrderWithNotifications(order, notifications));
        }
        return result;
    }

    public async Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(int orderId, string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        // Scope the order to its buyer: not-yours is indistinguishable from not-found.
        var order = await _orders.FirstOrDefaultAsync(new OrderByIdForBuyerSpecification(orderId, buyerId), cancellationToken);
        if (order is null)
        {
            throw new EntityNotFoundException($"Order {orderId} was not found.");
        }

        var notifications = await _notifications.ListAsync(new NotificationsByOrderSpecification(orderId), cancellationToken);
        await RefreshDeliveryStatesAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        // Idempotency: a repeat under the same key returns the message already produced.
        var priorForKey = await _notifications.FirstOrDefaultAsync(
            new NotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (priorForKey is not null)
        {
            _logger.LogInformation("Resend for idempotency key already satisfied by notification {NotificationId}.", priorForKey.Id);
            return priorForKey;
        }

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (original is null)
        {
            throw new EntityNotFoundException($"Notification {notificationId} was not found.");
        }
        if (original.ContentDisposed || string.IsNullOrEmpty(original.Body))
        {
            throw new NotificationContentDisposedException(
                $"Notification {notificationId} has no content to resend (it was disposed).");
        }

        var resend = new OrderNotification(original.OrderId, original.BuyerId, NotificationKind.Resend, original.RecipientNumber, original.Body!);
        resend.SetIdempotencyKey(idempotencyKey);

        // Persist the key first so a concurrent repeat cannot slip through and send twice.
        await _notifications.AddAsync(resend, cancellationToken);
        try
        {
            var result = await _smsGateway.SendAsync(original.RecipientNumber, original.Body!, cancellationToken);
            resend.RecordDispatch(result.ProviderMessageSid, result.Status, result.ErrorCode, result.ErrorMessage);
        }
        catch (Exception)
        {
            resend.RecordDispatch(null, "failed", null, "The resend could not be submitted to the provider.");
            _logger.LogWarning("Failed to submit resend for notification {NotificationId}.", original.Id);
        }
        await _notifications.UpdateAsync(resend, cancellationToken);
        return resend;
    }

    public async Task DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            throw new EntityNotFoundException($"Notification {notificationId} was not found.");
        }
        if (notification.ContentDisposed)
        {
            return; // already disposed — idempotent
        }

        // Redact at the provider first; only mark disposed locally once the provider-side content
        // is gone, so we never claim disposal we did not achieve. A provider failure surfaces.
        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            await _smsGateway.RedactContentAsync(notification.ProviderMessageSid!, cancellationToken);
        }

        notification.DisposeContent();
        await _notifications.UpdateAsync(notification, cancellationToken);
        _logger.LogInformation("Disposed content of notification {NotificationId}.", notificationId);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default)
    {
        if (toUtc < fromUtc)
        {
            throw new OrderRequestException("'to' must not be earlier than 'from'.");
        }

        // Ask the provider for its record of messages sent from our configured number in range.
        var providerMessages = await _smsGateway.ListOutboundAsync(fromUtc, toUtc, cancellationToken);
        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid))
            .GroupBy(m => m.Sid)
            .ToDictionary(g => g.Key, g => g.First());

        // What eShop believes it sent = notifications from the range that the provider accepted.
        var notifications = await _notifications.ListAsync(new NotificationsCreatedBetweenSpecification(fromUtc, toUtc), cancellationToken);
        var eShopBySid = notifications
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();
        var eShopOnly = new List<ReconciliationEntry>();

        foreach (var (sid, providerMessage) in providerBySid)
        {
            if (eShopBySid.TryGetValue(sid, out var notification))
            {
                matched.Add(new ReconciliationEntry(
                    sid, providerMessage.Status, providerMessage.DateSent,
                    KnownToProvider: true, KnownToEShop: true,
                    notification.Id, notification.OrderId, notification.Kind.ToString(), notification.ProviderStatus));
            }
            else
            {
                providerOnly.Add(new ReconciliationEntry(
                    sid, providerMessage.Status, providerMessage.DateSent,
                    KnownToProvider: true, KnownToEShop: false,
                    NotificationId: null, OrderId: null, Kind: null, EShopStatus: null));
            }
        }

        foreach (var (sid, notification) in eShopBySid)
        {
            if (!providerBySid.ContainsKey(sid))
            {
                eShopOnly.Add(new ReconciliationEntry(
                    sid, ProviderStatus: null, ProviderDateSent: null,
                    KnownToProvider: false, KnownToEShop: true,
                    notification.Id, notification.OrderId, notification.Kind.ToString(), notification.ProviderStatus));
            }
        }

        return new ReconciliationReport(
            fromUtc, toUtc,
            ProviderCount: providerBySid.Count,
            EShopCount: eShopBySid.Count,
            MatchedCount: matched.Count,
            Matched: matched,
            ProviderOnly: providerOnly,
            EShopOnly: eShopOnly);
    }

    // ---- helpers -------------------------------------------------------------------------

    private async Task<string?> GetRecipientAsync(string buyerId, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        // Most recently registered number wins (the spec orders by CreatedAt desc).
        return numbers.Count > 0 ? numbers[0].PhoneNumber : null;
    }

    private async Task SendImmediateAsync(Order order, string buyerId, NotificationKind kind, string body, CancellationToken cancellationToken)
    {
        var recipient = await GetRecipientAsync(buyerId, cancellationToken);
        if (recipient is null)
        {
            _logger.LogInformation("Order {OrderId}: no contact number on file; {Kind} SMS skipped.", order.Id, kind);
            return;
        }

        var notification = new OrderNotification(order.Id, buyerId, kind, recipient, body);
        await _notifications.AddAsync(notification, cancellationToken);
        try
        {
            var result = await _smsGateway.SendAsync(recipient, body, cancellationToken);
            notification.RecordDispatch(result.ProviderMessageSid, result.Status, result.ErrorCode, result.ErrorMessage);
        }
        catch (Exception)
        {
            // A message that cannot be sent must never fail the underlying order operation.
            notification.RecordDispatch(null, "failed", null, "The message could not be submitted to the provider.");
            _logger.LogWarning("Order {OrderId}: {Kind} SMS could not be submitted.", order.Id, kind);
        }
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    private async Task ScheduleFollowUpAsync(Order order, CancellationToken cancellationToken)
    {
        var recipient = await GetRecipientAsync(order.BuyerId, cancellationToken);
        if (recipient is null)
        {
            return;
        }

        var sendAt = DateTimeOffset.UtcNow.AddDays(FollowUpDelayDays);
        var body = FollowUpBody(order);
        var notification = new OrderNotification(order.Id, order.BuyerId, NotificationKind.DeliveryFollowUp, recipient, body);
        notification.MarkScheduled(sendAt);
        await _notifications.AddAsync(notification, cancellationToken);
        try
        {
            var result = await _smsGateway.ScheduleAsync(recipient, body, sendAt, cancellationToken);
            notification.RecordDispatch(result.ProviderMessageSid, result.Status, result.ErrorCode, result.ErrorMessage);
        }
        catch (Exception)
        {
            notification.RecordDispatch(null, "failed", null, "The follow-up could not be scheduled with the provider.");
            _logger.LogWarning("Order {OrderId}: delivery follow-up could not be scheduled.", order.Id);
        }
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    private async Task CancelFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var notifications = await _notifications.ListAsync(new NotificationsByOrderSpecification(orderId), cancellationToken);
        var pending = notifications.Where(n =>
            n.Kind == NotificationKind.DeliveryFollowUp &&
            n.IsScheduled && !n.IsCancelled &&
            !string.IsNullOrEmpty(n.ProviderMessageSid));

        foreach (var notification in pending)
        {
            try
            {
                await _smsGateway.CancelScheduledAsync(notification.ProviderMessageSid!, cancellationToken);
                notification.MarkCancelled("canceled");
                await _notifications.UpdateAsync(notification, cancellationToken);
                _logger.LogInformation("Order {OrderId}: called off scheduled follow-up {NotificationId}.", orderId, notification.Id);
            }
            catch (Exception)
            {
                // Surface loudly but do not fail the cancellation of the order itself.
                _logger.LogWarning("Order {OrderId}: FAILED to call off scheduled follow-up {NotificationId} — it may still be delivered.", orderId, notification.Id);
            }
        }
    }

    private async Task RefreshDeliveryStatesAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid))
            {
                continue;
            }
            try
            {
                var state = await _smsGateway.FetchStateAsync(notification.ProviderMessageSid!, cancellationToken);
                if (state is not null)
                {
                    notification.UpdateDeliveryState(state.Status, state.ErrorCode, state.ErrorMessage);
                    await _notifications.UpdateAsync(notification, cancellationToken);
                }
            }
            catch (Exception)
            {
                _logger.LogWarning("Failed to refresh delivery state for notification {NotificationId}.", notification.Id);
            }
        }
    }

    private static string OrderPlacedBody(Order order) =>
        $"eShop: your order #{order.Id} has been placed. Total {order.Total().ToString("C", CultureInfo.GetCultureInfo("en-US"))}. Thank you for shopping with us!";

    private static string DispatchedBody(Order order) =>
        $"eShop: good news — your order #{order.Id} is on its way!";

    private static string FollowUpBody(Order order) =>
        $"eShop: how did the delivery of your order #{order.Id} go? We'd love your feedback.";

    private static string CancelledBody(Order order) =>
        $"eShop: your order #{order.Id} has been cancelled. If this was unexpected, please contact support.";
}
