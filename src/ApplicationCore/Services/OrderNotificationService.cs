using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    // "A few days later" for the delivery follow-up (inside Twilio's 15min–7day scheduling window).
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    // Provider statuses that are still in flight and worth refreshing from the provider.
    private static readonly HashSet<string> NonTerminalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "queued", "sending", "accepted", "scheduled", "sent", "receiving"
    };

    // Provider statuses that mean the message did not reach the shopper (resend-eligible).
    private static readonly HashSet<string> FailedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "failed", "undelivered", "canceled"
    };

    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<SmsNotification> _notifications;
    private readonly IResendIdempotencyStore _idempotencyStore;
    private readonly ISmsProvider _sms;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IRepository<ContactNumber> contactNumbers,
        IRepository<SmsNotification> notifications,
        IResendIdempotencyStore idempotencyStore,
        ISmsProvider sms,
        IUriComposer uriComposer,
        IAppLogger<OrderNotificationService> logger)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _idempotencyStore = idempotencyStore;
        _sms = sms;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineItem> lines, Address shipToAddress, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(shipToAddress, nameof(shipToAddress));
        if (lines is null || lines.Count == 0)
        {
            throw new ArgumentException("An order must have at least one line.", nameof(lines));
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(ids), ct);

        var items = new List<OrderItem>();
        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new ArgumentException($"Quantity for catalog item {line.CatalogItemId} must be positive.", nameof(lines));
            }

            // Cross-operation invariant: an ordered line must reference a real catalog item.
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw new ArgumentException($"Catalog item {line.CatalogItemId} does not exist.", nameof(lines));

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            items.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, shipToAddress, items);
        await _orders.AddAsync(order, ct);
        _logger.LogInformation("Placed order {OrderId} for buyer {Buyer}.", order.Id, buyerId);

        await SendImmediateToBuyerAsync(order, NotificationKind.OrderPlaced, BuildBody(NotificationKind.OrderPlaced, order.Id), ct);
        return order.Id;
    }

    public async Task<OrderActionOutcome> DispatchOrderAsync(int orderId, CancellationToken ct)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct);
        if (order is null)
        {
            return OrderActionOutcome.OrderNotFound;
        }

        // Gate the outbound effects on a real transition — a re-dispatch does nothing and sends nothing.
        if (!order.TryMarkDispatched())
        {
            return OrderActionOutcome.NoChange;
        }

        await _orders.UpdateAsync(order, ct); // persist the transition before the provider calls
        _logger.LogInformation("Order {OrderId} marked dispatched.", orderId);

        await SendImmediateToBuyerAsync(order, NotificationKind.OrderDispatched, BuildBody(NotificationKind.OrderDispatched, order.Id), ct);
        await ScheduleFollowUpToBuyerAsync(order, ct);
        return OrderActionOutcome.Applied;
    }

    public async Task<OrderActionOutcome> CancelOrderAsync(int orderId, CancellationToken ct)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct);
        if (order is null)
        {
            return OrderActionOutcome.OrderNotFound;
        }

        if (!order.TryMarkCancelled())
        {
            return OrderActionOutcome.NoChange;
        }

        await _orders.UpdateAsync(order, ct);
        _logger.LogInformation("Order {OrderId} marked cancelled.", orderId);

        // Call off any follow-up that has not yet gone out — asking how a delivery went for a
        // cancelled order is exactly the incident to prevent. Do this before the cancel message.
        await CancelPendingFollowUpsAsync(order.Id, ct);

        await SendImmediateToBuyerAsync(order, NotificationKind.OrderCancelled, BuildBody(NotificationKind.OrderCancelled, order.Id), ct);
        return OrderActionOutcome.Applied;
    }

    public async Task<int?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var original = await _notifications.FirstOrDefaultAsync(new SmsNotificationByIdSpecification(notificationId), ct);
        if (original is null)
        {
            return null;
        }

        var body = original.Body ?? BuildBody(original.Kind, original.OrderId);

        // Create the local row for the resend BEFORE the provider call (write-order).
        var resend = new SmsNotification(original.OwnerId, original.OrderId, original.Recipient, NotificationKind.Resend, body);
        resend.MarkResendOf(original.Id, idempotencyKey);
        await _notifications.AddAsync(resend, ct);

        // Claim the caller-supplied key. The claim is a primary-key insert whose duplicate is
        // rejected by the store and caught there (never a check-then-act read). A repeat under the
        // same key returns the first result and sends no second message.
        var existingNotificationId = await _idempotencyStore.TryClaimAsync(idempotencyKey, resend.Id, ct);
        if (existingNotificationId is not null)
        {
            await _notifications.DeleteAsync(resend, ct);
            _logger.LogInformation("Resend under an already-used idempotency key; no second message sent.");
            return existingNotificationId.Value;
        }

        var result = await _sms.SendAsync(original.Recipient, body, ct);
        await ApplySendResultAsync(resend, result, ct);
        return resend.Id;
    }

    public async Task<bool> DisposeContentAsync(int notificationId, CancellationToken ct)
    {
        var notification = await _notifications.FirstOrDefaultAsync(new SmsNotificationByIdSpecification(notificationId), ct);
        if (notification is null)
        {
            return false;
        }

        if (notification.ContentRedacted)
        {
            return true; // already disposed
        }

        // Remove the retrievable text at the provider too (throws if it could not be done), then
        // clear it locally. The fact a message was sent, and what became of it, survives.
        if (!string.IsNullOrEmpty(notification.ProviderSid))
        {
            await _sms.RedactContentAsync(notification.ProviderSid!, ct);
        }

        notification.MarkContentRedacted();
        await _notifications.UpdateAsync(notification, ct);
        _logger.LogInformation("Disposed of the content of notification {Id}.", notificationId);
        return true;
    }

    public async Task<IReadOnlyList<OrderWithNotifications>> GetOrdersForBuyerAsync(string buyerId, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var orders = await _orders.ListAsync(new CustomerOrdersSpecification(buyerId), ct);
        var result = new List<OrderWithNotifications>();
        foreach (var order in orders)
        {
            var notifications = await _notifications.ListAsync(new SmsNotificationsByOrderSpecification(order.Id), ct);
            result.Add(new OrderWithNotifications(order, notifications));
        }

        return result;
    }

    public async Task<IReadOnlyList<SmsNotification>?> GetOrderNotificationsAsync(int orderId, string requestingBuyerId, bool isAdmin, CancellationToken ct)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct);
        if (order is null)
        {
            return null;
        }

        // Shopper-scoped: a non-admin caller only sees their own order's notifications.
        if (!isAdmin && order.BuyerId != requestingBuyerId)
        {
            return null;
        }

        var notifications = await _notifications.ListAsync(new SmsNotificationsByOrderSpecification(orderId), ct);

        // Refresh in-flight messages so the report reflects the provider's current outcome — proving
        // a later request can act on and report on the state the provider owns.
        foreach (var notification in notifications)
        {
            await RefreshOutcomeIfNeededAsync(notification, ct);
        }

        return notifications;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        // Populate the provider send-time on our own recent rows that don't have it yet, so both
        // sides of the reconciliation filter on the same clock (the provider's send time).
        var candidates = await _notifications.ListAsync(
            new SmsNotificationsWithProviderSidCreatedBetweenSpecification(from.AddDays(-7), to.AddDays(1)), ct);
        foreach (var notification in candidates.Where(n => n.ProviderDateSent is null))
        {
            await RefreshOutcomeIfNeededAsync(notification, ct, forceFetch: true);
        }

        var localInRange = await _notifications.ListAsync(new SmsNotificationsSentInRangeSpecification(from, to), ct);
        var providerResult = await _sms.ListSentMessagesAsync(from, to, ct);

        var localBySid = localInRange
            .Where(n => !string.IsNullOrEmpty(n.ProviderSid))
            .GroupBy(n => n.ProviderSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var providerBySid = providerResult.Messages
            .Where(m => !string.IsNullOrEmpty(m.Sid))
            .GroupBy(m => m.Sid!)
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new List<ReconciliationEntry>();
        var inProviderOnly = new List<ReconciliationEntry>();
        var inEShopOnly = new List<ReconciliationEntry>();

        foreach (var (sid, msg) in providerBySid)
        {
            if (localBySid.TryGetValue(sid, out var local))
            {
                matched.Add(new ReconciliationEntry
                {
                    ProviderSid = sid,
                    ProviderStatus = msg.Status,
                    NotificationId = local.Id,
                    LocalState = local.State.ToString(),
                    ProviderDateSent = msg.DateSent
                });
            }
            else
            {
                inProviderOnly.Add(new ReconciliationEntry
                {
                    ProviderSid = sid,
                    ProviderStatus = msg.Status,
                    ProviderDateSent = msg.DateSent
                });
            }
        }

        foreach (var (sid, local) in localBySid)
        {
            if (!providerBySid.ContainsKey(sid))
            {
                inEShopOnly.Add(new ReconciliationEntry
                {
                    ProviderSid = sid,
                    ProviderStatus = local.ProviderStatus,
                    NotificationId = local.Id,
                    LocalState = local.State.ToString(),
                    ProviderDateSent = local.ProviderDateSent
                });
            }
        }

        return new ReconciliationReport
        {
            From = from,
            To = to,
            Matched = matched,
            InProviderOnly = inProviderOnly,
            InEShopOnly = inEShopOnly,
            Truncated = providerResult.Truncated,
            ProviderPagesFetched = providerResult.PagesFetched
        };
    }

    // ---- helpers ----

    private async Task SendImmediateToBuyerAsync(Order order, NotificationKind kind, string body, CancellationToken ct)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByOwnerSpecification(order.BuyerId), ct);
        if (numbers.Count == 0)
        {
            // A shopper with no number on file is simply not messaged.
            return;
        }

        foreach (var number in numbers)
        {
            var notification = new SmsNotification(order.BuyerId, order.Id, number.PhoneNumber, kind, body);
            await _notifications.AddAsync(notification, ct); // local row before the provider call
            var result = await _sms.SendAsync(number.PhoneNumber, body, ct);
            await ApplySendResultAsync(notification, result, ct);
        }
    }

    private async Task ScheduleFollowUpToBuyerAsync(Order order, CancellationToken ct)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByOwnerSpecification(order.BuyerId), ct);
        if (numbers.Count == 0)
        {
            return;
        }

        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        var body = BuildBody(NotificationKind.DeliveryFollowUp, order.Id);

        foreach (var number in numbers)
        {
            var notification = new SmsNotification(order.BuyerId, order.Id, number.PhoneNumber, NotificationKind.DeliveryFollowUp, body);
            await _notifications.AddAsync(notification, ct);

            var result = await _sms.ScheduleAsync(number.PhoneNumber, body, sendAt, ct);
            if (result.Outcome == SmsSendOutcome.Accepted)
            {
                notification.MarkScheduled(result.ProviderSid, result.Status);
            }
            else if (result.Outcome == SmsSendOutcome.Rejected)
            {
                notification.MarkFailed(result.ErrorCode, result.ErrorMessage);
            }
            else
            {
                notification.MarkUnknown(result.ErrorMessage);
            }

            await _notifications.UpdateAsync(notification, ct);
        }
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken ct)
    {
        var followUps = await _notifications.ListAsync(new ScheduledFollowUpsByOrderSpecification(orderId), ct);
        foreach (var followUp in followUps)
        {
            if (string.IsNullOrEmpty(followUp.ProviderSid))
            {
                continue;
            }

            var result = await _sms.CancelScheduledAsync(followUp.ProviderSid!, ct);
            if (result.Outcome == SmsSendOutcome.Accepted)
            {
                followUp.MarkCancelled(result.Status);
            }
            else
            {
                // The follow-up may still go out — this is the incident to prevent, so log loudly.
                _logger.LogWarning(
                    "Could not cancel scheduled follow-up notification {Id} for order {OrderId}; it may still be delivered.",
                    followUp.Id, orderId);
            }

            await _notifications.UpdateAsync(followUp, ct);
        }
    }

    private async Task ApplySendResultAsync(SmsNotification notification, SmsSendResult result, CancellationToken ct)
    {
        switch (result.Outcome)
        {
            case SmsSendOutcome.Accepted:
                notification.MarkSent(result.ProviderSid, result.Status, result.DateSent);
                break;

            case SmsSendOutcome.Rejected:
                notification.MarkFailed(result.ErrorCode, result.ErrorMessage);
                break;

            default:
                // Unknown: settle it in code by re-reading provider state before concluding.
                notification.MarkUnknown(result.ErrorMessage);
                try
                {
                    var found = await _sms.FindRecentAsync(notification.Recipient, notification.CreatedAt.AddMinutes(-2), ct);
                    if (found is not null)
                    {
                        notification.UpdateProviderOutcome(found.Sid, found.Status, found.ErrorCode, found.ErrorMessage, found.DateSent);
                    }
                }
                catch (SmsProviderException)
                {
                    // Still unreachable — leave it Unknown for later reconciliation.
                }
                break;
        }

        await _notifications.UpdateAsync(notification, ct);
    }

    private async Task RefreshOutcomeIfNeededAsync(SmsNotification notification, CancellationToken ct, bool forceFetch = false)
    {
        if (string.IsNullOrEmpty(notification.ProviderSid) || notification.ContentRedacted)
        {
            return;
        }

        var status = notification.ProviderStatus;
        var shouldRefresh = forceFetch
            || notification.State == NotificationState.Unknown
            || status is null
            || NonTerminalStatuses.Contains(status);
        if (!shouldRefresh)
        {
            return;
        }

        try
        {
            var current = await _sms.FetchAsync(notification.ProviderSid!, ct);
            if (current is not null)
            {
                notification.UpdateProviderOutcome(current.Sid, current.Status, current.ErrorCode, current.ErrorMessage, current.DateSent);
                await _notifications.UpdateAsync(notification, ct);
            }
        }
        catch (SmsProviderException)
        {
            // Best-effort refresh; keep the last known outcome if the provider is unreachable.
        }
    }

    public static bool IsFailedStatus(string? status) => status is not null && FailedStatuses.Contains(status);

    private static string BuildBody(NotificationKind kind, int orderId) => kind switch
    {
        NotificationKind.OrderPlaced => $"eShopOnWeb: your order #{orderId} has been placed. Thank you for shopping with us!",
        NotificationKind.OrderDispatched => $"eShopOnWeb: good news - your order #{orderId} is on its way!",
        NotificationKind.DeliveryFollowUp => $"eShopOnWeb: how did the delivery of order #{orderId} go? We'd love your feedback.",
        NotificationKind.OrderCancelled => $"eShopOnWeb: your order #{orderId} has been cancelled. Contact support if this is unexpected.",
        _ => $"eShopOnWeb: an update about your order #{orderId}."
    };
}
