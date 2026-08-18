using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Places orders and drives the SMS that go out as an order moves. Any message that cannot be sent
/// is recorded as a failed notification and never bubbles up to fail the order operation itself.
/// </summary>
public class OrderNotificationService : IOrderNotificationService
{
    /// <summary>How far in the future the "how did delivery go?" follow-up is queued with the provider.</summary>
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private static readonly HashSet<string> TerminalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "delivered",
        "undelivered",
        "failed",
        NotificationDeliveryStatus.Canceled,   // "canceled"
        NotificationDeliveryStatus.SendFailed   // "send_failed"
    };

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _catalogItemRepository;
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IRepository<Notification> _notificationRepository;
    private readonly ISmsGateway _smsGateway;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> catalogItemRepository,
        IRepository<ContactNumber> contactNumberRepository,
        IRepository<Notification> notificationRepository,
        ISmsGateway smsGateway,
        IUriComposer uriComposer,
        IAppLogger<OrderNotificationService> logger)
    {
        _orderRepository = orderRepository;
        _catalogItemRepository = catalogItemRepository;
        _contactNumberRepository = contactNumberRepository;
        _notificationRepository = notificationRepository;
        _smsGateway = smsGateway;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines, Address shipToAddress, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(shipToAddress, nameof(shipToAddress));
        if (lines is null || lines.Count == 0)
        {
            throw new ArgumentException("An order must contain at least one item.", nameof(lines));
        }
        if (lines.Any(l => l.Quantity <= 0))
        {
            throw new ArgumentException("Every order line must have a quantity of at least one.", nameof(lines));
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItemRepository.ListAsync(new CatalogItemsSpecification(ids), ct);

        var items = new List<OrderItem>();
        foreach (var line in lines)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId);
            if (catalogItem is null)
            {
                throw new ArgumentException($"Catalog item {line.CatalogItemId} does not exist.", nameof(lines));
            }

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            items.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, shipToAddress, items);
        order = await _orderRepository.AddAsync(order, ct);

        // Tell the shopper the order was placed. A send failure must not fail the placement.
        await SendImmediateForOrderAsync(order, NotificationKind.OrderPlaced, ct);

        return order;
    }

    public async Task<OrderActionResult> DispatchAsync(int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, ct);
        if (order is null)
        {
            return OrderActionResult.NotFound;
        }
        if (order.Status != OrderStatus.Placed)
        {
            return OrderActionResult.InvalidState;
        }

        order.MarkDispatched();
        await _orderRepository.UpdateAsync(order, ct);

        // Tell the shopper it is on its way, then queue the delivery follow-up WITH THE PROVIDER for later.
        await SendImmediateForOrderAsync(order, NotificationKind.OrderDispatched, ct);
        await ScheduleFollowUpAsync(order, ct);

        return OrderActionResult.Success;
    }

    public async Task<OrderActionResult> CancelAsync(int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, ct);
        if (order is null)
        {
            return OrderActionResult.NotFound;
        }
        if (order.Status == OrderStatus.Cancelled)
        {
            return OrderActionResult.InvalidState;
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, ct);

        // Call off any delivery follow-up that has not gone out BEFORE anything else — a cancelled order
        // must never trigger a "how did delivery go?" message.
        await CancelPendingFollowUpsAsync(order.Id, ct);

        // Then tell the shopper it was cancelled.
        await SendImmediateForOrderAsync(order, NotificationKind.OrderCancelled, ct);

        return OrderActionResult.Success;
    }

    public async Task<IReadOnlyList<MyOrderView>> GetMyOrdersAsync(string buyerId, CancellationToken ct)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);

        var views = new List<MyOrderView>();
        foreach (var order in orders)
        {
            var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderSpecification(order.Id), ct);
            await RefreshDeliveryOutcomesAsync(notifications, ct);

            views.Add(new MyOrderView(
                order.Id,
                order.Status.ToString(),
                order.OrderDate,
                order.Total(),
                notifications.Select(ToView).ToList()));
        }

        return views;
    }

    public async Task<IReadOnlyList<NotificationView>?> GetOrderNotificationsAsync(int orderId, string callerId, CancellationToken ct)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, ct);
        if (order is null || !string.Equals(order.BuyerId, callerId, StringComparison.Ordinal))
        {
            // A shopper can only see their own order's notifications.
            return null;
        }

        var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderSpecification(orderId), ct);
        await RefreshDeliveryOutcomesAsync(notifications, ct);
        return notifications.Select(ToView).ToList();
    }

    public async Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        // Idempotency: a repeat under the same key returns the message the first attempt produced.
        var existing = await _notificationRepository.FirstOrDefaultAsync(
            new NotificationByIdempotencyKeySpecification(idempotencyKey), ct);
        if (existing is not null)
        {
            return new ResendResult(ResendOutcome.Duplicate, existing);
        }

        var original = await _notificationRepository.GetByIdAsync(notificationId, ct);
        if (original is null)
        {
            return new ResendResult(ResendOutcome.NotFound, null);
        }

        // If the original content was disposed of, regenerate the standard body for its kind.
        var body = original.Body ?? BuildBody(original.Kind, original.OrderId);

        var resend = new Notification(
            original.OwnerId,
            original.OrderId,
            original.Kind,
            original.ToNumber,
            body,
            idempotencyKey: idempotencyKey,
            resendOfNotificationId: original.Id);

        try
        {
            var result = await _smsGateway.SendAsync(original.ToNumber, body, ct);
            resend.RecordAccepted(result.ProviderMessageSid, result.Status, result.ErrorCode, result.ErrorMessage);
        }
        catch (SmsGatewayException ex)
        {
            // A resend that cannot go out still produces a record the operator can see and act on.
            resend.RecordSendFailed(ex.ProviderErrorCode, ex.Message);
            _logger.LogWarning("Resend of notification {NotificationId} failed to send: provider status {Status}.",
                notificationId, ex.StatusCode);
        }

        resend = await _notificationRepository.AddAsync(resend, ct);
        return new ResendResult(ResendOutcome.Sent, resend);
    }

    public async Task<bool> RedactContentAsync(int notificationId, CancellationToken ct)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, ct);
        if (notification is null)
        {
            return false;
        }

        if (notification.ProviderMessageSid is not null && !notification.ContentRedacted)
        {
            // Disposal is the operation itself: if the provider cannot dispose of the text, surface the
            // failure rather than clearing the local copy and claiming the content is gone everywhere.
            await _smsGateway.RedactContentAsync(notification.ProviderMessageSid, ct);
        }

        notification.RedactContent();
        await _notificationRepository.UpdateAsync(notification, ct);
        return true;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var providerMessages = await _smsGateway.ListSentMessagesAsync(from, to, ct);
        var localNotifications = await _notificationRepository.ListAsync(new NotificationsSentInRangeSpecification(from, to), ct);

        var localBySid = localNotifications
            .Where(n => n.ProviderMessageSid is not null)
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var providerSids = new HashSet<string>(providerMessages.Select(m => m.Sid), StringComparer.Ordinal);

        var matched = new List<ReconciliationMatch>();
        var providerOnly = new List<ReconciliationDiscrepancy>();
        foreach (var message in providerMessages)
        {
            if (localBySid.TryGetValue(message.Sid, out var local))
            {
                matched.Add(new ReconciliationMatch(message.Sid, message.Status, local.Id, local.DeliveryStatus));
            }
            else
            {
                providerOnly.Add(new ReconciliationDiscrepancy(
                    message.Sid, message.Status, null,
                    "The provider has this message; eShop has no record of sending it."));
            }
        }

        var eShopOnly = localBySid.Values
            .Where(n => !providerSids.Contains(n.ProviderMessageSid!))
            .Select(n => new ReconciliationDiscrepancy(
                n.ProviderMessageSid, n.DeliveryStatus, n.Id,
                "eShop recorded sending this from its number; the provider did not return it for this range."))
            .ToList();

        return new ReconciliationReport(
            from,
            to,
            _smsGateway.SendingNumber,
            providerMessages.Count,
            localBySid.Count,
            matched.Count,
            providerOnly,
            eShopOnly,
            matched);
    }

    // --- helpers -------------------------------------------------------------------------------

    private async Task SendImmediateForOrderAsync(Order order, NotificationKind kind, CancellationToken ct)
    {
        var numbers = await _contactNumberRepository.ListAsync(new ContactNumbersByOwnerSpecification(order.BuyerId), ct);
        if (numbers.Count == 0)
        {
            // A shopper with no number on file is simply not messaged.
            _logger.LogInformation("No contact number on file for order {OrderId}; {Kind} notification not sent.", order.Id, kind);
            return;
        }

        var body = BuildBody(kind, order.Id);
        foreach (var number in numbers)
        {
            var notification = new Notification(order.BuyerId, order.Id, kind, number.E164Number, body);
            try
            {
                var result = await _smsGateway.SendAsync(number.E164Number, body, ct);
                notification.RecordAccepted(result.ProviderMessageSid, result.Status, result.ErrorCode, result.ErrorMessage);
            }
            catch (SmsGatewayException ex)
            {
                notification.RecordSendFailed(ex.ProviderErrorCode, ex.Message);
                _logger.LogWarning("Could not send {Kind} notification for order {OrderId}: provider status {Status}.",
                    kind, order.Id, ex.StatusCode);
            }

            await _notificationRepository.AddAsync(notification, ct);
        }
    }

    private async Task ScheduleFollowUpAsync(Order order, CancellationToken ct)
    {
        var numbers = await _contactNumberRepository.ListAsync(new ContactNumbersByOwnerSpecification(order.BuyerId), ct);
        if (numbers.Count == 0)
        {
            return;
        }

        var body = BuildBody(NotificationKind.DeliveryFollowUp, order.Id);
        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);

        foreach (var number in numbers)
        {
            var notification = new Notification(
                order.BuyerId, order.Id, NotificationKind.DeliveryFollowUp, number.E164Number, body,
                isScheduled: true, scheduledFor: sendAt);
            try
            {
                var result = await _smsGateway.ScheduleAsync(number.E164Number, body, sendAt, ct);
                notification.RecordAccepted(result.ProviderMessageSid, result.Status, result.ErrorCode, result.ErrorMessage);
            }
            catch (SmsGatewayException ex)
            {
                notification.RecordSendFailed(ex.ProviderErrorCode, ex.Message);
                _logger.LogWarning("Could not schedule delivery follow-up for order {OrderId}: provider status {Status}.",
                    order.Id, ex.StatusCode);
            }

            await _notificationRepository.AddAsync(notification, ct);
        }
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken ct)
    {
        var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderSpecification(orderId), ct);
        foreach (var notification in notifications)
        {
            var isCancellableFollowUp = notification.Kind == NotificationKind.DeliveryFollowUp
                && notification.IsScheduled
                && notification.ProviderMessageSid is not null
                && string.Equals(notification.DeliveryStatus, "scheduled", StringComparison.OrdinalIgnoreCase);
            if (!isCancellableFollowUp)
            {
                continue;
            }

            try
            {
                await _smsGateway.CancelScheduledAsync(notification.ProviderMessageSid!, ct);
                notification.MarkCanceled();
                await _notificationRepository.UpdateAsync(notification, ct);
            }
            catch (SmsGatewayException ex)
            {
                // Best effort: if the provider already sent it, it cannot be recalled. Never fail the cancel.
                _logger.LogWarning("Could not cancel scheduled follow-up {NotificationId} for order {OrderId}: provider status {Status}.",
                    notification.Id, orderId, ex.StatusCode);
            }
        }
    }

    private async Task RefreshDeliveryOutcomesAsync(IReadOnlyList<Notification> notifications, CancellationToken ct)
    {
        foreach (var notification in notifications)
        {
            if (notification.ProviderMessageSid is null || TerminalStatuses.Contains(notification.DeliveryStatus))
            {
                continue;
            }

            try
            {
                var status = await _smsGateway.FetchStatusAsync(notification.ProviderMessageSid, ct);
                notification.UpdateDeliveryStatus(status.Status, status.ErrorCode, status.ErrorMessage);
                await _notificationRepository.UpdateAsync(notification, ct);
            }
            catch (SmsGatewayException ex)
            {
                // Reporting must not fail because a single status read could not be refreshed.
                _logger.LogWarning("Could not refresh delivery outcome for notification {NotificationId}: provider status {Status}.",
                    notification.Id, ex.StatusCode);
            }
        }
    }

    private static NotificationView ToView(Notification n) => new(
        n.Id,
        n.OrderId,
        n.Kind.ToString(),
        n.DeliveryStatus,
        n.ProviderMessageSid,
        n.ProviderErrorCode,
        n.ProviderErrorMessage,
        n.IsScheduled,
        n.ScheduledFor,
        n.ContentRedacted,
        n.CreatedAt,
        n.UpdatedAt);

    private static string BuildBody(NotificationKind kind, int orderId) => kind switch
    {
        NotificationKind.OrderPlaced => $"eShop: your order #{orderId} has been placed. Thank you for shopping with us!",
        NotificationKind.OrderDispatched => $"eShop: good news — your order #{orderId} is on its way!",
        NotificationKind.DeliveryFollowUp => $"eShop: how did the delivery of your order #{orderId} go? We'd love your feedback.",
        NotificationKind.OrderCancelled => $"eShop: your order #{orderId} has been cancelled. If this wasn't expected, please get in touch.",
        _ => $"eShop: an update about your order #{orderId}."
    };
}
