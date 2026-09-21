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

/// <summary>
/// Orchestrates order-progress SMS notifications. Every provider interaction goes through
/// <see cref="ISmsGateway"/>, and no notification failure is ever allowed to fail the underlying order
/// operation. The local notification record is always written before the provider is called, so a
/// provider-side message is never stranded without a record.
/// </summary>
public class OrderNotificationService : IOrderNotificationService
{
    // The existing storefront checkout uses a fixed ship-to address; the API mirrors that.
    private static readonly Address DefaultShipToAddress =
        new("123 Main St.", "Kent", "OH", "United States", "44240");

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _catalogRepository;
    private readonly IRepository<ContactNumber> _contactRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly ISmsGateway _gateway;
    private readonly IUriComposer _uriComposer;
    private readonly NotificationOptions _options;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> catalogRepository,
        IRepository<ContactNumber> contactRepository,
        IRepository<OrderNotification> notificationRepository,
        ISmsGateway gateway,
        IUriComposer uriComposer,
        NotificationOptions options,
        IAppLogger<OrderNotificationService> logger)
    {
        _orderRepository = orderRepository;
        _catalogRepository = catalogRepository;
        _contactRepository = contactRepository;
        _notificationRepository = notificationRepository;
        _gateway = gateway;
        _uriComposer = uriComposer;
        _options = options;
        _logger = logger;
    }

    // ---- Flow 1: contact numbers -------------------------------------------------------------------

    public async Task<ContactNumber> RegisterContactNumberAsync(string buyerId, string rawNumber, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawNumber))
            throw new OrderValidationException("A phone number is required.");

        // Reject an unusable destination here, and store the provider's canonical form — not what was typed.
        var validation = await _gateway.ValidateNumberAsync(rawNumber, ct);
        if (!validation.IsValid || string.IsNullOrEmpty(validation.CanonicalE164))
            throw new OrderValidationException("The phone number is not a usable destination.");

        var contact = new ContactNumber(buyerId, validation.CanonicalE164!);
        await _contactRepository.AddAsync(contact, ct);
        _logger.LogInformation("Registered contact number {ContactNumberId} for a shopper.", contact.Id);
        return contact;
    }

    public async Task<IReadOnlyList<ContactNumber>> GetContactNumbersAsync(string buyerId, CancellationToken ct) =>
        await _contactRepository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), ct);

    public async Task<bool> RemoveContactNumberAsync(string buyerId, int contactNumberId, CancellationToken ct)
    {
        var contact = await _contactRepository.GetByIdAsync(contactNumberId, ct);
        if (contact is null || contact.BuyerId != buyerId)
            return false;   // never reveal or act on another shopper's number

        await _contactRepository.DeleteAsync(contact, ct);
        _logger.LogInformation("Removed contact number {ContactNumberId}.", contactNumberId);
        return true;
    }

    // ---- Flow 2: order lifecycle + messages --------------------------------------------------------

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> lines, CancellationToken ct)
    {
        if (lines is null || lines.Count == 0)
            throw new OrderValidationException("An order must contain at least one item.");
        if (lines.Any(l => l.Quantity <= 0))
            throw new OrderValidationException("Each order line must have a positive quantity.");

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogRepository.ListAsync(new CatalogItemsSpecification(ids), ct);

        var items = new List<OrderItem>();
        foreach (var line in lines)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw new OrderValidationException($"Catalog item {line.CatalogItemId} does not exist.");

            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            items.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, DefaultShipToAddress, items);
        await _orderRepository.AddAsync(order, ct);
        _logger.LogInformation("Placed order {OrderId}.", order.Id);

        await NotifyAsync(order, NotificationType.OrderPlaced, ct);
        return order;
    }

    public async Task<OrderTransition> DispatchOrderAsync(int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, ct);
        if (order is null)
            return OrderTransition.NotFound;

        // Side effects are gated on a real transition: a repeat dispatch changes nothing and sends nothing.
        if (!order.MarkDispatched())
            return OrderTransition.NoChange;

        await _orderRepository.UpdateAsync(order, ct);
        _logger.LogInformation("Dispatched order {OrderId}.", orderId);

        await NotifyAsync(order, NotificationType.OrderDispatched, ct);
        await ScheduleFollowUpAsync(order, ct);
        return OrderTransition.Changed;
    }

    public async Task<OrderTransition> CancelOrderAsync(int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, ct);
        if (order is null)
            return OrderTransition.NotFound;

        if (!order.MarkCancelled())
            return OrderTransition.NoChange;

        await _orderRepository.UpdateAsync(order, ct);
        _logger.LogInformation("Cancelled order {OrderId}.", orderId);

        // Call off any not-yet-sent follow-up BEFORE anything else, so it can never reach the shopper.
        await CancelPendingFollowUpsAsync(order, ct);
        await NotifyAsync(order, NotificationType.OrderCancelled, ct);
        return OrderTransition.Changed;
    }

    public async Task<IReadOnlyList<OrderWithNotifications>> GetMyOrdersAsync(string buyerId, CancellationToken ct)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);
        if (orders.Count == 0)
            return Array.Empty<OrderWithNotifications>();

        var notifications = await _notificationRepository.ListAsync(
            new OrderNotificationsByOrdersSpecification(orders.Select(o => o.Id)), ct);

        await RefreshDeliveryAsync(notifications, ct);

        var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => (IReadOnlyList<OrderNotification>)g.ToList());
        return orders
            .Select(o => new OrderWithNotifications(
                o, byOrder.TryGetValue(o.Id, out var ns) ? ns : Array.Empty<OrderNotification>()))
            .ToList();
    }

    public async Task<IReadOnlyList<OrderNotification>?> GetOwnedOrderNotificationsAsync(string buyerId, int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, ct);
        if (order is null || order.BuyerId != buyerId)
            return null;   // don't reveal another shopper's order

        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByOrderSpecification(orderId), ct);
        await RefreshDeliveryAsync(notifications, ct);
        return notifications;
    }

    // ---- Flow 3: operator actions ------------------------------------------------------------------

    public async Task<OrderNotification?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new OrderValidationException("An idempotency key is required.");

        var original = await _notificationRepository.GetByIdAsync(notificationId, ct);
        if (original is null)
            return null;

        // Repeating the request under the same key must not send again: return the earlier resend.
        var existing = await _notificationRepository.FirstOrDefaultAsync(
            new OrderNotificationByResendKeySpecification(idempotencyKey), ct);
        if (existing is not null)
            return existing;

        var body = original.Body ?? BuildMessageBody(original.Type, original.OrderId);

        // Local record first, carrying the idempotency claim (a UNIQUE index backs it — see EF config).
        var resend = new OrderNotification(
            original.BuyerId, original.OrderId, original.Type, original.ToNumberE164, body,
            isScheduledFollowUp: false, resendIdempotencyKey: idempotencyKey, resendOfNotificationId: original.Id);

        try
        {
            await _notificationRepository.AddAsync(resend, ct);
        }
        catch (Exception ex) when (IsUniquenessViolation(ex))
        {
            // A concurrent request under the same key won the race — return its result, do not send again.
            var winner = await _notificationRepository.FirstOrDefaultAsync(
                new OrderNotificationByResendKeySpecification(idempotencyKey), ct);
            if (winner is not null)
                return winner;
            throw;
        }

        await SendNotificationAsync(resend, ct);
        _logger.LogInformation(
            "Re-sent notification {ResendId} for original {OriginalId}.", resend.Id, original.Id);
        return resend;
    }

    public async Task<bool> DisposeContentAsync(int notificationId, CancellationToken ct)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, ct);
        if (notification is null)
            return false;

        // Dispose at the provider first (so the text is genuinely gone there), then clear locally.
        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            try
            {
                await _gateway.DisposeContentAsync(notification.ProviderMessageSid!, ct);
            }
            catch (SmsGatewayException ex)
            {
                _logger.LogWarning("Could not dispose content at the provider for notification {Id}: {Message}",
                    notificationId, ex.Message);
                throw;   // the content is NOT gone at the provider — report the failure rather than pretend
            }
        }

        notification.MarkContentDisposed();
        await _notificationRepository.UpdateAsync(notification, ct);
        _logger.LogInformation("Disposed content for notification {Id}.", notificationId);
        return true;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        // Ask the provider for its own record of messages from our number in the range.
        var listing = await _gateway.ListSentAsync(from, to, ct);
        var providerBySid = listing.Messages
            .GroupBy(m => m.Sid)
            .ToDictionary(g => g.Key, g => g.First());

        // Local side: notifications that carry a provider Sid.
        var localAll = await _notificationRepository.ListAsync(ct);
        var localWithSid = localAll.Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid)).ToList();
        var localBySid = localWithSid
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();
        var eShopOnly = new List<ReconciliationEntry>();
        var outOfWindow = new List<ReconciliationEntry>();

        foreach (var p in listing.Messages)
        {
            if (localBySid.TryGetValue(p.Sid, out var local))
                matched.Add(new ReconciliationEntry(
                    p.Sid, local.Id, local.OrderId, p.RawStatus, local.DeliveryState.ToString(), p.DateSent));
            else
                providerOnly.Add(new ReconciliationEntry(p.Sid, null, null, p.RawStatus, null, p.DateSent));
        }

        foreach (var local in localWithSid)
        {
            if (providerBySid.ContainsKey(local.ProviderMessageSid!))
                continue;   // already counted as matched

            // Same clock on both sides: the provider's send time. Unknown/out-of-range is its own category.
            var sentAt = local.ProviderDateSent;
            var inWindow = sentAt.HasValue && sentAt.Value >= from && sentAt.Value <= to;
            var entry = new ReconciliationEntry(
                local.ProviderMessageSid, local.Id, local.OrderId, local.ProviderStatus,
                local.DeliveryState.ToString(), sentAt);
            (inWindow ? eShopOnly : outOfWindow).Add(entry);
        }

        return new ReconciliationReport(from, to, listing.Truncated, matched, providerOnly, eShopOnly, outOfWindow);
    }

    // ---- internals ---------------------------------------------------------------------------------

    /// <summary>Raise an immediate message per registered number. Never throws — a send failure must not
    /// fail the order operation, and a shopper with no number on file is simply not messaged.</summary>
    private async Task NotifyAsync(Order order, NotificationType type, CancellationToken ct)
    {
        var numbers = await _contactRepository.ListAsync(new ContactNumbersByBuyerSpecification(order.BuyerId), ct);
        if (numbers.Count == 0)
            return;

        var body = BuildMessageBody(type, order.Id);
        foreach (var number in numbers)
        {
            var notification = new OrderNotification(
                order.BuyerId, order.Id, type, number.E164Number, body, isScheduledFollowUp: false);
            await _notificationRepository.AddAsync(notification, ct);   // local record first
            await SendNotificationAsync(notification, ct);
        }
    }

    /// <summary>Schedule the "how did delivery go?" follow-up with the provider for a few days later.</summary>
    private async Task ScheduleFollowUpAsync(Order order, CancellationToken ct)
    {
        var numbers = await _contactRepository.ListAsync(new ContactNumbersByBuyerSpecification(order.BuyerId), ct);
        if (numbers.Count == 0)
            return;

        var body = BuildMessageBody(NotificationType.DeliveryFollowUp, order.Id);
        var sendAt = DateTimeOffset.UtcNow.AddDays(_options.FollowUpDelayDays);

        foreach (var number in numbers)
        {
            var notification = new OrderNotification(
                order.BuyerId, order.Id, NotificationType.DeliveryFollowUp, number.E164Number, body,
                isScheduledFollowUp: true);
            await _notificationRepository.AddAsync(notification, ct);   // local record first

            try
            {
                var result = await _gateway.ScheduleAsync(number.E164Number, body, sendAt, ct);
                notification.RecordProviderResult(
                    result.Sid, result.RawStatus, result.State, result.ErrorCode, result.ErrorMessage, result.DateSent);
            }
            catch (SmsGatewayException ex)
            {
                RecordSendFailure(notification, ex);
            }

            await _notificationRepository.UpdateAsync(notification, ct);
        }
    }

    /// <summary>Cancel every not-yet-sent scheduled follow-up for the order so it can never reach the shopper.</summary>
    private async Task CancelPendingFollowUpsAsync(Order order, CancellationToken ct)
    {
        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByOrderSpecification(order.Id), ct);
        foreach (var n in notifications.Where(n =>
                     n.IsScheduledFollowUp
                     && !string.IsNullOrEmpty(n.ProviderMessageSid)
                     && n.DeliveryState == NotificationDeliveryState.Pending))
        {
            try
            {
                var result = await _gateway.CancelScheduledAsync(n.ProviderMessageSid!, ct);
                n.MarkCancelled(result.RawStatus);
            }
            catch (SmsGatewayException ex)
            {
                _logger.LogWarning("Could not cancel scheduled follow-up {Id}: {Message}", n.Id, ex.Message);
                // Best effort; leave the record as-is. It stays visible as pending for reconciliation.
            }

            await _notificationRepository.UpdateAsync(n, ct);
        }
    }

    /// <summary>Send one already-persisted immediate notification, swallowing any failure.</summary>
    private async Task SendNotificationAsync(OrderNotification notification, CancellationToken ct)
    {
        try
        {
            var result = await _gateway.SendAsync(notification.ToNumberE164, notification.Body!, ct);
            notification.RecordProviderResult(
                result.Sid, result.RawStatus, result.State, result.ErrorCode, result.ErrorMessage, result.DateSent);
        }
        catch (SmsGatewayException ex)
        {
            RecordSendFailure(notification, ex);
        }
        catch (Exception ex)
        {
            // Absolutely never let a notification failure escape into the order operation.
            _logger.LogWarning("Unexpected error sending notification {Id}: {Message}", notification.Id, ex.Message);
            notification.RecordSendUnknown("An unexpected error occurred while sending.");
        }

        await _notificationRepository.UpdateAsync(notification, ct);
    }

    private void RecordSendFailure(OrderNotification notification, SmsGatewayException ex)
    {
        if (ex.OutcomeUnknown)
            notification.RecordSendUnknown(ex.Message);   // may have been received — surfaced by reconciliation
        else
            _logger.LogWarning("Provider rejected notification {Id}: {Message}", notification.Id, ex.Message);
    }

    /// <summary>Poll the provider for the current outcome of any non-terminal, sent notifications. Best effort.</summary>
    private async Task RefreshDeliveryAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken ct)
    {
        foreach (var n in notifications.Where(n =>
                     !string.IsNullOrEmpty(n.ProviderMessageSid)
                     && (n.DeliveryState == NotificationDeliveryState.Pending
                         || n.DeliveryState == NotificationDeliveryState.Unknown)))
        {
            try
            {
                var result = await _gateway.FetchAsync(n.ProviderMessageSid!, ct);
                n.RefreshDelivery(result.RawStatus, result.State, result.ErrorCode, result.ErrorMessage, result.DateSent);
                await _notificationRepository.UpdateAsync(n, ct);
            }
            catch (SmsGatewayException ex)
            {
                _logger.LogWarning("Could not refresh delivery for notification {Id}: {Message}", n.Id, ex.Message);
            }
        }
    }

    private static string BuildMessageBody(NotificationType type, int orderId) => type switch
    {
        NotificationType.OrderPlaced => $"eShop: your order #{orderId} has been placed. Thank you!",
        NotificationType.OrderDispatched => $"eShop: good news — your order #{orderId} is on its way!",
        NotificationType.OrderCancelled => $"eShop: your order #{orderId} has been cancelled.",
        NotificationType.DeliveryFollowUp => $"eShop: how did the delivery of your order #{orderId} go? We'd love your feedback.",
        _ => $"eShop: an update about your order #{orderId}."
    };

    private static bool IsUniquenessViolation(Exception ex)
    {
        // EF Core surfaces a unique-index violation as DbUpdateException; match by name to avoid an
        // ApplicationCore dependency on EntityFrameworkCore.
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e.GetType().Name.Contains("DbUpdateException", StringComparison.Ordinal))
                return true;
        }
        return false;
    }
}
