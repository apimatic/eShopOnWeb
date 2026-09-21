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

public class OrderNotificationService : IOrderNotificationService
{
    // A whole-operation budget for the SMS provider calls a single request makes (their per-attempt
    // timeouts add up), linked to the caller's own cancellation.
    private static readonly TimeSpan ProviderBudget = TimeSpan.FromSeconds(30);

    // Twilio's message scheduling window is 15 minutes … 7 days out; "a few days" -> 3 days.
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    // Provider delivery statuses that are final — no point re-reading them.
    private static readonly HashSet<string> TerminalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "delivered", "failed", "undelivered", "canceled", "cancelled",
        OrderNotification.LocalStatus.SendFailed
    };

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _catalogItemRepository;
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly ISmsGateway _smsGateway;
    private readonly IUriComposer _uriComposer;
    private readonly IResendIdempotencyGuard _idempotencyGuard;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> catalogItemRepository,
        IRepository<ContactNumber> contactNumberRepository,
        IRepository<OrderNotification> notificationRepository,
        ISmsGateway smsGateway,
        IUriComposer uriComposer,
        IResendIdempotencyGuard idempotencyGuard,
        IAppLogger<OrderNotificationService> logger)
    {
        _orderRepository = orderRepository;
        _catalogItemRepository = catalogItemRepository;
        _contactNumberRepository = contactNumberRepository;
        _notificationRepository = notificationRepository;
        _smsGateway = smsGateway;
        _uriComposer = uriComposer;
        _idempotencyGuard = idempotencyGuard;
        _logger = logger;
    }

    public async Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> items, ShippingAddressInput? shipTo, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (items is null || items.Count == 0)
        {
            throw new OrderPlacementException("An order must contain at least one item.");
        }
        if (items.Any(i => i.Quantity <= 0))
        {
            throw new OrderPlacementException("Every order item must have a quantity of at least one.");
        }

        var ids = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItemRepository.ListAsync(new CatalogItemsSpecification(ids), ct);
        var byId = catalogItems.ToDictionary(c => c.Id);

        var missing = ids.Where(id => !byId.ContainsKey(id)).ToArray();
        if (missing.Length > 0)
        {
            throw new OrderPlacementException($"Unknown catalog item(s): {string.Join(", ", missing)}.");
        }

        var orderItems = new List<OrderItem>();
        foreach (var line in items)
        {
            var catalogItem = byId[line.CatalogItemId];
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var address = shipTo is null
            ? new Address("N/A", "N/A", "N/A", "N/A", "N/A")
            : new Address(shipTo.Street, shipTo.City, shipTo.State, shipTo.Country, shipTo.ZipCode);

        var order = new Order(buyerId, address, orderItems);
        order = await _orderRepository.AddAsync(order, ct);

        _logger.LogInformation("Placed order {OrderId} for buyer {BuyerId}.", order.Id, buyerId);

        await NotifyImmediateAsync(order.Id, buyerId, NotificationKind.OrderPlaced,
            $"eShop: your order #{order.Id} has been placed. Thank you for shopping with us!", ct);

        return order.Id;
    }

    public async Task<bool> DispatchOrderAsync(int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, ct);
        if (order is null)
        {
            return false;
        }

        var existing = await _notificationRepository.ListAsync(new OrderNotificationsByOrderSpecification(orderId), ct);
        // No-op transition: already dispatched -> do not fire the notification (or the follow-up) again.
        if (existing.Any(n => n.Kind == NotificationKind.OrderDispatched))
        {
            _logger.LogInformation("Order {OrderId} already dispatched; skipping duplicate notification.", orderId);
            return true;
        }

        await NotifyImmediateAsync(orderId, order.BuyerId, NotificationKind.OrderDispatched,
            $"eShop: good news — your order #{orderId} is on its way!", ct);

        // Queue the "how did delivery go" follow-up WITH THE PROVIDER for a few days later — not held here.
        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        await ScheduleFollowUpAsync(orderId, order.BuyerId,
            $"eShop: how did the delivery of order #{orderId} go? We'd love your feedback.", sendAt, ct);

        return true;
    }

    public async Task<bool> CancelOrderAsync(int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, ct);
        if (order is null)
        {
            return false;
        }

        var existing = await _notificationRepository.ListAsync(new OrderNotificationsByOrderSpecification(orderId), ct);
        if (existing.Any(n => n.Kind == NotificationKind.OrderCancelled))
        {
            _logger.LogInformation("Order {OrderId} already cancelled; skipping duplicate notification.", orderId);
            return true;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ProviderBudget);
        var opCt = cts.Token;

        // Call off any not-yet-sent follow-up so "how did delivery go" can never reach a cancelled order.
        var pending = await _notificationRepository.ListAsync(new PendingFollowUpsByOrderSpecification(orderId), opCt);
        foreach (var followUp in pending)
        {
            var cancelled = await _smsGateway.CancelScheduledAsync(followUp.MessageSid!, opCt);
            if (cancelled)
            {
                followUp.UpdateDeliveryState("canceled", null, null);
            }
            else
            {
                // Best-effort within the cancel flow; record the intent and refresh later.
                followUp.UpdateDeliveryState(OrderNotification.LocalStatus.CancelRequested, null, null);
                _logger.LogWarning("Could not confirm cancellation of scheduled follow-up notification {NotificationId} for order {OrderId}.", followUp.Id, orderId);
            }
            await _notificationRepository.UpdateAsync(followUp, opCt);
        }

        await NotifyImmediateAsync(orderId, order.BuyerId, NotificationKind.OrderCancelled,
            $"eShop: your order #{orderId} has been cancelled. Please contact us with any questions.", opCt);

        return true;
    }

    public async Task<IReadOnlyList<OrderSummaryView>> GetMyOrdersAsync(string buyerId, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);
        var result = new List<OrderSummaryView>();
        foreach (var order in orders)
        {
            var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByOrderSpecification(order.Id), ct);
            result.Add(new OrderSummaryView(
                order.Id,
                order.OrderDate,
                order.Total(),
                notifications.Select(ToView).ToList()));
        }
        return result;
    }

    public async Task<IReadOnlyList<OrderNotificationView>?> GetOrderNotificationsAsync(string buyerId, int orderId, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var order = await _orderRepository.GetByIdAsync(orderId, ct);
        if (order is null || order.BuyerId != buyerId)
        {
            return null; // Not the caller's order (or none) — one shopper never sees another's.
        }

        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByOrderSpecification(orderId), ct);
        await RefreshDeliveryStatesAsync(notifications, ct);
        return notifications.Select(ToView).ToList();
    }

    public async Task<ResendResult?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        // Serialise on the key so a repeat under the same key can never produce a second send.
        return await _idempotencyGuard.ExecuteAsync(idempotencyKey, async innerCt =>
        {
            var priorForKey = await _notificationRepository.FirstOrDefaultAsync(
                new OrderNotificationByIdempotencyKeySpecification(idempotencyKey), innerCt);
            if (priorForKey is not null)
            {
                // Same key already used — return the earlier result without sending again.
                return new ResendResult(priorForKey.Id, Deduplicated: true);
            }

            var original = await _notificationRepository.FirstOrDefaultAsync(
                new OrderNotificationByIdSpecification(notificationId), innerCt);
            if (original is null || original.ContentDisposed || string.IsNullOrEmpty(original.Body))
            {
                return (ResendResult?)null; // Nothing to re-send.
            }

            // Record our own claim first (carries the key), then call the provider, then settle it.
            var resend = new OrderNotification(original.OrderId, original.BuyerId, NotificationKind.Resend, original.ToNumber, original.Body!);
            resend.MarkAsResend(original.Id, idempotencyKey);
            resend = await _notificationRepository.AddAsync(resend, innerCt);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(innerCt);
            cts.CancelAfter(ProviderBudget);
            var dispatch = await _smsGateway.SendAsync(original.ToNumber, original.Body!, cts.Token);
            ApplyDispatchResult(resend, dispatch);
            await _notificationRepository.UpdateAsync(resend, innerCt);

            _logger.LogInformation("Re-sent notification {OriginalId} as {ResendId} (accepted={Accepted}).",
                original.Id, resend.Id, dispatch.Accepted);
            return new ResendResult(resend.Id, Deduplicated: false);
        }, ct);
    }

    public async Task<bool> DisposeContentAsync(int notificationId, CancellationToken ct)
    {
        var notification = await _notificationRepository.FirstOrDefaultAsync(
            new OrderNotificationByIdSpecification(notificationId), ct);
        if (notification is null)
        {
            return false;
        }

        if (notification.ContentDisposed)
        {
            return true; // Already disposed — idempotent.
        }

        if (!string.IsNullOrEmpty(notification.MessageSid))
        {
            // Redact at the provider so the text is no longer retrievable there either.
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(ProviderBudget);
            await _smsGateway.RedactContentAsync(notification.MessageSid!, cts.Token);
        }

        notification.MarkContentDisposed();
        await _notificationRepository.UpdateAsync(notification, ct);
        _logger.LogInformation("Disposed content of notification {NotificationId}.", notificationId);
        return true;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ProviderBudget);

        // Ask the provider for its record of messages sent FROM our configured number over the range.
        var providerResult = await _smsGateway.ListSentAsync(from, to, cts.Token);
        var providerBySid = providerResult.Messages
            .Where(m => !string.IsNullOrEmpty(m.Sid))
            .GroupBy(m => m.Sid)
            .ToDictionary(g => g.Key, g => g.First());

        // What eShop believes it sent (has a provider SID).
        var eShopSent = await _notificationRepository.ListAsync(new SentOrderNotificationsSpecification(), ct);
        var eShopBySid = eShopSent
            .Where(n => !string.IsNullOrEmpty(n.MessageSid))
            .GroupBy(n => n.MessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var inBoth = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();
        var eShopOnly = new List<ReconciliationEntry>();

        foreach (var providerMsg in providerBySid.Values)
        {
            if (eShopBySid.TryGetValue(providerMsg.Sid, out var eShopMatch))
            {
                inBoth.Add(new ReconciliationEntry(providerMsg.Sid, eShopMatch.Id, providerMsg.Status, eShopMatch.ChannelStatus, providerMsg.DateSent));
            }
            else
            {
                providerOnly.Add(new ReconciliationEntry(providerMsg.Sid, null, providerMsg.Status, null, providerMsg.DateSent));
            }
        }

        foreach (var eShopMsg in eShopBySid.Values)
        {
            if (providerBySid.ContainsKey(eShopMsg.MessageSid!))
            {
                continue; // already accounted for as InBoth
            }

            // Filter the eShop side on the SAME clock as the provider (its send time): a scheduled
            // follow-up's effective send time is when it is due, not when the row was written. Anything
            // outside the window is "out of window", not a discrepancy.
            var effectiveSendTime = eShopMsg.ScheduledSendAt ?? eShopMsg.CreatedAt;
            if (effectiveSendTime >= from && effectiveSendTime <= to)
            {
                eShopOnly.Add(new ReconciliationEntry(eShopMsg.MessageSid, eShopMsg.Id, null, eShopMsg.ChannelStatus, eShopMsg.ScheduledSendAt));
            }
        }

        return new ReconciliationReport(from, to, _smsGateway.SendingNumber, providerResult.Truncated, inBoth, providerOnly, eShopOnly);
    }

    // --- helpers -----------------------------------------------------------------

    /// <summary>Send an immediate notification to the buyer's number, recording where it got to. Never fails the caller.</summary>
    private async Task NotifyImmediateAsync(int orderId, string buyerId, NotificationKind kind, string body, CancellationToken ct)
    {
        var toNumber = await ResolveDestinationAsync(buyerId, ct);
        if (toNumber is null)
        {
            _logger.LogInformation("Buyer {BuyerId} has no number on file; order {OrderId} {Kind} not messaged.", buyerId, orderId, kind);
            return; // A shopper with no number on file is simply not messaged.
        }

        var notification = new OrderNotification(orderId, buyerId, kind, toNumber, body);
        notification = await _notificationRepository.AddAsync(notification, ct);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ProviderBudget);
        var dispatch = await _smsGateway.SendAsync(toNumber, body, cts.Token);
        ApplyDispatchResult(notification, dispatch);
        await _notificationRepository.UpdateAsync(notification, ct);
    }

    /// <summary>Queue the follow-up with the provider. Never fails the caller.</summary>
    private async Task ScheduleFollowUpAsync(int orderId, string buyerId, string body, DateTimeOffset sendAt, CancellationToken ct)
    {
        var toNumber = await ResolveDestinationAsync(buyerId, ct);
        if (toNumber is null)
        {
            return;
        }

        var notification = new OrderNotification(orderId, buyerId, NotificationKind.DeliveryFollowUp, toNumber, body);
        notification.MarkAsScheduledFollowUp(sendAt);
        notification = await _notificationRepository.AddAsync(notification, ct);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ProviderBudget);
        var dispatch = await _smsGateway.ScheduleAsync(toNumber, body, sendAt, cts.Token);
        ApplyDispatchResult(notification, dispatch);
        await _notificationRepository.UpdateAsync(notification, ct);
    }

    private async Task<string?> ResolveDestinationAsync(string buyerId, CancellationToken ct)
    {
        var latest = await _contactNumberRepository.FirstOrDefaultAsync(new LatestContactNumberByBuyerSpecification(buyerId), ct);
        return latest?.E164Number;
    }

    private static void ApplyDispatchResult(OrderNotification notification, SmsDispatchResult dispatch)
    {
        if (dispatch.Accepted && !string.IsNullOrEmpty(dispatch.MessageSid))
        {
            notification.MarkAccepted(dispatch.MessageSid!, dispatch.Status ?? string.Empty);
            if (dispatch.ErrorCode is not null || !string.IsNullOrEmpty(dispatch.ErrorDescription))
            {
                notification.UpdateDeliveryState(dispatch.Status, dispatch.ErrorCode, dispatch.ErrorDescription);
            }
        }
        else
        {
            notification.MarkSendFailed(dispatch.FailureReason);
        }
    }

    private async Task RefreshDeliveryStatesAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ProviderBudget);

        foreach (var notification in notifications)
        {
            if (notification.MessageSid is null || notification.ContentDisposed)
            {
                continue;
            }
            if (TerminalStatuses.Contains(notification.ChannelStatus))
            {
                continue; // Nothing more will change.
            }

            var status = await _smsGateway.FetchStatusAsync(notification.MessageSid, cts.Token);
            if (status.Found)
            {
                notification.UpdateDeliveryState(status.Status, status.ErrorCode, status.ErrorDescription);
                await _notificationRepository.UpdateAsync(notification, ct);
            }
        }
    }

    private static OrderNotificationView ToView(OrderNotification n) => new(
        n.Id,
        n.OrderId,
        n.Kind.ToString(),
        string.IsNullOrEmpty(n.ChannelStatus) ? "unknown" : n.ChannelStatus,
        n.MessageSid,
        n.ErrorCode,
        n.ErrorDescription,
        n.IsScheduled,
        n.ScheduledSendAt,
        n.ContentDisposed,
        n.CreatedAt,
        n.UpdatedAt);
}
