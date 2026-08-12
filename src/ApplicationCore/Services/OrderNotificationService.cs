using System;
using System.Collections.Generic;
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

public class OrderNotificationService : IOrderNotificationService
{
    // A default ship-to address for API-placed orders, mirroring the Web checkout which also uses a
    // fixed address. The order request itself carries only catalog items and quantities.
    private static readonly Address DefaultShipToAddress =
        new("123 Main St.", "Kent", "OH", "United States", "44240");

    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly ITwilioMessagingGateway _gateway;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IRepository<ContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        ITwilioMessagingGateway gateway,
        IUriComposer uriComposer,
        IAppLogger<OrderNotificationService> logger)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _gateway = gateway;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
        {
            throw new InvalidOrderRequestException("An order must contain at least one line.");
        }

        // Collapse duplicate catalog ids and reject non-positive quantities.
        var requested = lines
            .GroupBy(l => l.CatalogItemId)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));
        if (requested.Values.Any(q => q <= 0))
        {
            throw new InvalidOrderRequestException("Every order line must have a positive quantity.");
        }

        var catalogItems = await _catalogItems.ListAsync(
            new CatalogItemsSpecification(requested.Keys.ToArray()), ct);
        var missing = requested.Keys.Except(catalogItems.Select(c => c.Id)).ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOrderRequestException($"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var items = catalogItems.Select(catalogItem =>
        {
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, requested[catalogItem.Id]);
        }).ToList();

        var order = new Order(buyerId, DefaultShipToAddress, items);
        await _orders.AddAsync(order, ct);
        _logger.LogInformation("Placed order {OrderId} for a shopper.", order.Id);

        await SendNowAsync(order, NotificationKind.OrderPlaced, ct);
        return order;
    }

    public async Task<Order> DispatchOrderAsync(int orderId, CancellationToken ct = default)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct)
            ?? throw new EntityNotFoundException("order", orderId);

        order.MarkDispatched();
        await _orders.UpdateAsync(order, ct);
        _logger.LogInformation("Dispatched order {OrderId}.", order.Id);

        await SendNowAsync(order, NotificationKind.OrderDispatched, ct);
        await ScheduleFollowUpAsync(order, ct);
        return order;
    }

    public async Task<Order> CancelOrderAsync(int orderId, CancellationToken ct = default)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct)
            ?? throw new EntityNotFoundException("order", orderId);

        order.MarkCancelled();
        await _orders.UpdateAsync(order, ct);
        _logger.LogInformation("Cancelled order {OrderId}.", order.Id);

        // Call off any delivery follow-up that has not yet gone out — a cancelled order must never
        // trigger a "how did the delivery go?" message.
        await CancelPendingFollowUpsAsync(order.Id, ct);
        await SendNowAsync(order, NotificationKind.OrderCancelled, ct);
        return order;
    }

    public async Task<IReadOnlyList<OrderWithNotifications>> GetOrdersForBuyerAsync(string buyerId, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);
        var result = new List<OrderWithNotifications>(orders.Count);
        foreach (var order in orders)
        {
            var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderSpecification(order.Id), ct);
            await RefreshStatusesAsync(notifications, ct);
            result.Add(new OrderWithNotifications(order, notifications));
        }

        return result;
    }

    public async Task<Order?> GetOrderAsync(int orderId, CancellationToken ct = default)
    {
        return await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct);
    }

    public async Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(int orderId, CancellationToken ct = default)
    {
        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderSpecification(orderId), ct);
        await RefreshStatusesAsync(notifications, ct);
        return notifications;
    }

    public async Task<OrderNotification?> GetNotificationAsync(int notificationId, CancellationToken ct = default)
    {
        return await _notifications.GetByIdAsync(notificationId, ct);
    }

    public async Task<OrderNotification> ResendNotificationAsync(int notificationId, string idempotencyKey, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        // Idempotency: a repeat under the same key returns the message that key already produced,
        // without sending a second one.
        var alreadyDone = await _notifications.FirstOrDefaultAsync(
            new OrderNotificationByIdempotencyKeySpecification(idempotencyKey), ct);
        if (alreadyDone is not null)
        {
            _logger.LogInformation("Resend request for notification {NotificationId} matched an existing idempotency key; not sending again.", notificationId);
            return alreadyDone;
        }

        var original = await _notifications.GetByIdAsync(notificationId, ct)
            ?? throw new EntityNotFoundException("notification", notificationId);

        if (original.ContentDisposed || string.IsNullOrEmpty(original.Body))
        {
            throw new NotificationConflictException("The message content has been disposed of and cannot be resent.");
        }

        var resend = new OrderNotification(original.OrderId, original.BuyerId, original.Kind, original.ToNumber, original.Body);
        resend.MarkAsResendOf(original.Id, idempotencyKey);

        // A hard provider failure here propagates (the key is not consumed, so a retry is legitimate).
        var result = await _gateway.SendMessageAsync(original.ToNumber, original.Body, ct);
        resend.SetProviderResult(result.Sid, result.Status, result.ErrorCode, result.ErrorMessage);
        await _notifications.AddAsync(resend, ct);
        _logger.LogInformation("Resent notification {OriginalId} as {NewId}.", original.Id, resend.Id);
        return resend;
    }

    public async Task DisposeNotificationContentAsync(int notificationId, CancellationToken ct = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, ct)
            ?? throw new EntityNotFoundException("notification", notificationId);

        if (notification.ContentDisposed)
        {
            return;
        }

        // Dispose at the provider first so the text is no longer retrievable there; only then locally.
        // If there is no provider message (the send never reached the provider), there is nothing to redact.
        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            await _gateway.RedactMessageBodyAsync(notification.ProviderMessageSid, ct);
        }

        notification.MarkContentDisposed();
        await _notifications.UpdateAsync(notification, ct);
        _logger.LogInformation("Disposed of content for notification {NotificationId}.", notificationId);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var providerMessages = await _gateway.ListSentMessagesAsync(from, to, ct);
        var eShopNotifications = await _notifications.ListAsync(new OrderNotificationsCreatedBetweenSpecification(from, to), ct);

        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid))
            .GroupBy(m => m.Sid)
            .ToDictionary(g => g.Key, g => g.First());
        var eShopSids = eShopNotifications
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .Select(n => n.ProviderMessageSid!)
            .ToHashSet();

        var matched = new List<ReconciliationMatch>();
        var eShopOnly = new List<EShopOnlyEntry>();
        foreach (var notification in eShopNotifications)
        {
            if (!string.IsNullOrEmpty(notification.ProviderMessageSid) &&
                providerBySid.TryGetValue(notification.ProviderMessageSid, out var providerMessage))
            {
                matched.Add(new ReconciliationMatch(
                    notification.ProviderMessageSid!,
                    notification.Id,
                    notification.OrderId,
                    notification.Kind,
                    providerMessage.Status,
                    notification.ProviderStatus));
            }
            else
            {
                var reason = string.IsNullOrEmpty(notification.ProviderMessageSid)
                    ? "never_reached_provider"
                    : "not_in_provider_range";
                eShopOnly.Add(new EShopOnlyEntry(
                    notification.Id, notification.OrderId, notification.ProviderMessageSid,
                    notification.Kind, notification.ProviderStatus, reason));
            }
        }

        var providerOnly = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid) && !eShopSids.Contains(m.Sid))
            .Select(m => new ProviderOnlyEntry(m.Sid, m.Status, m.DateSent))
            .ToList();

        return new ReconciliationReport(
            from, to,
            providerMessages.Count,
            eShopNotifications.Count,
            matched,
            providerOnly,
            eShopOnly);
    }

    // ----- helpers -----

    /// <summary>Resolve the destination for a shopper: their most recently registered number, or null if none.</summary>
    private async Task<string?> ResolveDestinationAsync(string buyerId, CancellationToken ct)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), ct);
        return numbers.FirstOrDefault()?.E164Number;
    }

    /// <summary>
    /// Send an order-event SMS now and record it. A messaging failure is caught and recorded — it never
    /// propagates, so the underlying order operation always succeeds. A shopper with no number is not messaged.
    /// </summary>
    private async Task<OrderNotification?> SendNowAsync(Order order, NotificationKind kind, CancellationToken ct)
    {
        var toNumber = await ResolveDestinationAsync(order.BuyerId, ct);
        if (toNumber is null)
        {
            _logger.LogInformation("No contact number on file for order {OrderId}; skipping {Kind} SMS.", order.Id, kind);
            return null;
        }

        var body = NotificationMessages.For(kind, order.Id);
        var notification = new OrderNotification(order.Id, order.BuyerId, kind, toNumber, body);
        try
        {
            var result = await _gateway.SendMessageAsync(toNumber, body, ct);
            notification.SetProviderResult(result.Sid, result.Status, result.ErrorCode, result.ErrorMessage);
        }
        catch (Exception ex)
        {
            // Never fail the order operation because a message could not be sent.
            notification.MarkSendFailed(SafeReason(ex));
            _logger.LogWarning("Could not send {Kind} SMS for order {OrderId}.", kind, order.Id);
        }

        await _notifications.AddAsync(notification, ct);
        return notification;
    }

    /// <summary>Schedule the delivery follow-up ~3 days out and record it. Failures are caught, not propagated.</summary>
    private async Task ScheduleFollowUpAsync(Order order, CancellationToken ct)
    {
        var toNumber = await ResolveDestinationAsync(order.BuyerId, ct);
        if (toNumber is null)
        {
            _logger.LogInformation("No contact number on file for order {OrderId}; skipping delivery follow-up.", order.Id);
            return;
        }

        var body = NotificationMessages.For(NotificationKind.DeliveryFollowUp, order.Id);
        var notification = new OrderNotification(order.Id, order.BuyerId, NotificationKind.DeliveryFollowUp, toNumber, body);
        var sendAt = DateTimeOffset.UtcNow.AddDays(3);
        try
        {
            var result = await _gateway.ScheduleMessageAsync(toNumber, body, sendAt, ct);
            notification.SetScheduled(result.Sid, sendAt);
        }
        catch (Exception ex)
        {
            notification.MarkSendFailed(SafeReason(ex));
            _logger.LogWarning("Could not schedule delivery follow-up for order {OrderId}.", order.Id);
        }

        await _notifications.AddAsync(notification, ct);
    }

    /// <summary>Cancel any scheduled, not-yet-sent delivery follow-up for the order. Best-effort; never propagates.</summary>
    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken ct)
    {
        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderSpecification(orderId), ct);
        foreach (var followUp in notifications.Where(n =>
            n.Kind == NotificationKind.DeliveryFollowUp &&
            !string.IsNullOrEmpty(n.ProviderMessageSid) &&
            string.Equals(n.ProviderStatus, NotificationStatuses.Scheduled, StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var state = await _gateway.CancelScheduledMessageAsync(followUp.ProviderMessageSid!, ct);
                followUp.UpdateStatus(string.IsNullOrWhiteSpace(state.Status) ? NotificationStatuses.Canceled : state.Status);
                await _notifications.UpdateAsync(followUp, ct);
                _logger.LogInformation("Called off scheduled follow-up notification {NotificationId} for order {OrderId}.", followUp.Id, orderId);
            }
            catch (Exception)
            {
                // If it already went out or the provider refuses, do not fail the cancellation of the order.
                _logger.LogWarning("Could not call off scheduled follow-up notification {NotificationId} for order {OrderId}.", followUp.Id, orderId);
            }
        }
    }

    /// <summary>Best-effort refresh of each notification's delivery outcome from the provider.</summary>
    private async Task RefreshStatusesAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken ct)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid) || IsTerminal(notification.ProviderStatus))
            {
                continue;
            }

            try
            {
                var state = await _gateway.FetchMessageStateAsync(notification.ProviderMessageSid, ct);
                notification.UpdateStatus(state.Status, state.ErrorCode, state.ErrorMessage);
                await _notifications.UpdateAsync(notification, ct);
            }
            catch (Exception)
            {
                // Reporting must not fail because a provider read failed; keep the last-known status.
            }
        }
    }

    private static bool IsTerminal(string? status) =>
        string.Equals(status, "delivered", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, NotificationStatuses.Undelivered, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, NotificationStatuses.Failed, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, NotificationStatuses.Canceled, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, NotificationStatuses.SendFailed, StringComparison.OrdinalIgnoreCase);

    /// <summary>A caller/log-safe reason string that never carries a number, token, or raw provider payload.</summary>
    private static string SafeReason(Exception ex) =>
        ex is NotificationProviderException ? ex.Message : "An unexpected error occurred while contacting the messaging provider.";
}
