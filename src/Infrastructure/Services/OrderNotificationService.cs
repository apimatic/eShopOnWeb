using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Places orders (reusing the existing Order/OrderItem model) and drives the SMS notifications that go
/// out as an order moves. A message that cannot be sent is recorded as failed but never fails the order
/// operation. Dispatch/cancel gate their side effects on a real state transition.
/// </summary>
public class OrderNotificationService : IOrderNotificationService
{
    // "A few days later" for the delivery follow-up — within the provider's scheduling window.
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);
    private const int MaxReconciliationPages = 50;

    // Provider delivery outcomes that are settled — no point re-fetching them, and a scheduled follow-up
    // in one of these states has already gone out (or been called off) so it is not cancellable.
    private static readonly HashSet<string> TerminalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "delivered", "failed", "undelivered", "canceled", "received", "read"
    };

    private readonly IRepository<Order> _orderRepository;
    private readonly IReadRepository<CatalogItem> _catalogRepository;
    private readonly IReadRepository<ContactNumber> _contactNumberRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IUriComposer _uriComposer;
    private readonly ISmsSender _smsSender;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orderRepository,
        IReadRepository<CatalogItem> catalogRepository,
        IReadRepository<ContactNumber> contactNumberRepository,
        IRepository<OrderNotification> notificationRepository,
        IUriComposer uriComposer,
        ISmsSender smsSender,
        IAppLogger<OrderNotificationService> logger)
    {
        _orderRepository = orderRepository;
        _catalogRepository = catalogRepository;
        _contactNumberRepository = contactNumberRepository;
        _notificationRepository = notificationRepository;
        _uriComposer = uriComposer;
        _smsSender = smsSender;
        _logger = logger;
    }

    public async Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> lines, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
        {
            throw new ArgumentException("An order must contain at least one item.", nameof(lines));
        }

        if (lines.Any(l => l.Quantity <= 0))
        {
            throw new ArgumentException("Every order line must have a quantity of at least 1.", nameof(lines));
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        var itemsById = catalogItems.ToDictionary(c => c.Id);

        // Cross-operation invariant: every line must reference a catalog item that exists.
        var unknown = ids.Where(id => !itemsById.ContainsKey(id)).ToArray();
        if (unknown.Length > 0)
        {
            throw new ArgumentException($"Unknown catalog item id(s): {string.Join(", ", unknown)}.", nameof(lines));
        }

        var orderItems = lines.Select(line =>
        {
            var catalogItem = itemsById[line.CatalogItemId];
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        // The API carries no address; the existing Order aggregate requires one. Placeholder ship-to.
        var shipToAddress = new Address("N/A (placed via API)", "N/A", "N/A", "N/A", "00000");
        var order = new Order(buyerId, shipToAddress, orderItems);
        await _orderRepository.AddAsync(order, cancellationToken);

        _logger.LogInformation("Order {OrderId} placed for buyer via API.", order.Id);

        await NotifyAllNumbersAsync(buyerId, order.Id, NotificationKind.OrderPlaced, cancellationToken);

        return order.Id;
    }

    public async Task<OrderActionResult> DispatchAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            return new OrderActionResult(OrderActionStatus.NotFound, null);
        }

        if (!order.Dispatch())
        {
            _logger.LogInformation("Dispatch no-op for order {OrderId} (status {Status}).", order.Id, order.Status);
            return new OrderActionResult(OrderActionStatus.AlreadyInState, await BuildOrderSummaryAsync(order, cancellationToken));
        }

        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Order {OrderId} dispatched.", order.Id);

        // Gated on the real transition above: tell the shopper, then queue the follow-up a few days out.
        await NotifyAllNumbersAsync(order.BuyerId, order.Id, NotificationKind.Dispatched, cancellationToken);
        await ScheduleFollowUpsAsync(order.BuyerId, order.Id, cancellationToken);

        return new OrderActionResult(OrderActionStatus.Applied, await BuildOrderSummaryAsync(order, cancellationToken));
    }

    public async Task<OrderActionResult> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            return new OrderActionResult(OrderActionStatus.NotFound, null);
        }

        if (!order.Cancel())
        {
            _logger.LogInformation("Cancel no-op for order {OrderId} (already cancelled).", order.Id);
            return new OrderActionResult(OrderActionStatus.AlreadyInState, await BuildOrderSummaryAsync(order, cancellationToken));
        }

        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Order {OrderId} cancelled.", order.Id);

        await NotifyAllNumbersAsync(order.BuyerId, order.Id, NotificationKind.Cancelled, cancellationToken);
        // A follow-up that has not yet gone out must never reach the shopper.
        await CancelPendingFollowUpsAsync(order.Id, cancellationToken);

        return new OrderActionResult(OrderActionStatus.Applied, await BuildOrderSummaryAsync(order, cancellationToken));
    }

    public async Task<IReadOnlyList<OrderSummary>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersSpecification(buyerId), cancellationToken);
        var summaries = new List<OrderSummary>(orders.Count);
        foreach (var order in orders)
        {
            summaries.Add(await BuildOrderSummaryAsync(order, cancellationToken));
        }

        return summaries;
    }

    public async Task<IReadOnlyList<NotificationSummary>?> GetOrderNotificationsAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);

        // A shopper only ever sees their own order's notifications.
        if (order is null || order.BuyerId != buyerId)
        {
            return null;
        }

        var notifications = await GetAndRefreshNotificationsAsync(orderId, cancellationToken);
        return notifications.Select(ToSummary).ToList();
    }

    public async Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var original = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (original is null)
        {
            return new ResendResult(NotificationFound: false, NotificationId: null, WasDuplicate: false);
        }

        // Idempotency fast-path: a prior request under the same key already produced a notification.
        var existing = await _notificationRepository.FirstOrDefaultAsync(
            new OrderNotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (existing is not null)
        {
            return new ResendResult(NotificationFound: true, NotificationId: existing.Id, WasDuplicate: true);
        }

        var resend = new OrderNotification(
            original.OrderId,
            original.BuyerId,
            NotificationKind.Resend,
            original.ToPhoneNumber,
            isScheduled: false,
            idempotencyKey: idempotencyKey,
            resendOfNotificationId: original.Id);

        // Claim the key by inserting the row first. A unique index on IdempotencyKey rejects a concurrent
        // second claim; on that rejection we re-read by key and return the winner rather than sending again.
        try
        {
            await _notificationRepository.AddAsync(resend, cancellationToken);
        }
        catch (DbUpdateException)
        {
            var winner = await _notificationRepository.FirstOrDefaultAsync(
                new OrderNotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
            if (winner is not null)
            {
                return new ResendResult(NotificationFound: true, NotificationId: winner.Id, WasDuplicate: true);
            }

            throw;
        }

        var body = BuildBody(original.Kind, original.OrderId);
        try
        {
            var result = await _smsSender.SendAsync(original.ToPhoneNumber, body, cancellationToken);
            resend.RecordSent(result.Sid, result.Status, result.SentAtUtc, result.ErrorCode, result.ErrorMessage);
        }
        catch (SmsGatewayException ex)
        {
            resend.RecordSendFailure("send_failed", ex.Message);
            _logger.LogWarning("Re-send for notification {NotificationId} failed to reach the provider.", notificationId);
        }

        await _notificationRepository.UpdateAsync(resend, cancellationToken);
        return new ResendResult(NotificationFound: true, NotificationId: resend.Id, WasDuplicate: false);
    }

    public async Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            return false;
        }

        // Redact the text at the provider so it is no longer retrievable there either. If it never reached
        // the provider (no SID), there is nothing to redact remotely — record the disposal locally.
        if (!string.IsNullOrEmpty(notification.MessageSid))
        {
            await _smsSender.RedactContentAsync(notification.MessageSid, cancellationToken);
        }

        notification.MarkContentRedacted();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
        _logger.LogInformation("Content disposed for notification {NotificationId}.", notificationId);
        return true;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var providerMessages = new List<ProviderMessage>();
        int? page = null;
        string? pageToken = null;
        var pages = 0;
        var complete = true;

        while (true)
        {
            var pageResult = await _smsSender.ListSentMessagesAsync(from, to, page, pageToken, cancellationToken);
            providerMessages.AddRange(pageResult.Messages);
            pages++;

            if (!pageResult.HasMore)
            {
                break;
            }

            if (pages >= MaxReconciliationPages)
            {
                // Safety cap hit — the range was not fully walked. The caller learns this via `complete`.
                complete = false;
                _logger.LogWarning("Reconciliation truncated after {Pages} pages.", pages);
                break;
            }

            page = pageResult.NextPage;
            pageToken = pageResult.NextPageToken;
        }

        var eShopNotifications = await _notificationRepository.ListAsync(
            new SentNotificationsInRangeSpecification(from, to), cancellationToken);

        var eShopBySid = eShopNotifications
            .Where(n => !string.IsNullOrEmpty(n.MessageSid))
            .GroupBy(n => n.MessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid))
            .GroupBy(m => m.Sid!)
            .ToDictionary(g => g.Key, g => g.First());

        var entries = new List<ReconciliationEntry>();

        foreach (var message in providerMessages.Where(m => !string.IsNullOrEmpty(m.Sid)))
        {
            if (eShopBySid.TryGetValue(message.Sid!, out var notification))
            {
                entries.Add(new ReconciliationEntry(
                    message.Sid, message.Status, notification.ProviderStatus, notification.Id, ReconciliationMatch.Matched));
            }
            else
            {
                entries.Add(new ReconciliationEntry(
                    message.Sid, message.Status, null, null, ReconciliationMatch.OnlyAtProvider));
            }
        }

        foreach (var notification in eShopNotifications.Where(n => !string.IsNullOrEmpty(n.MessageSid)))
        {
            if (!providerBySid.ContainsKey(notification.MessageSid!))
            {
                entries.Add(new ReconciliationEntry(
                    notification.MessageSid, null, notification.ProviderStatus, notification.Id, ReconciliationMatch.OnlyAtEShop));
            }
        }

        return new ReconciliationReport(from, to, complete, providerMessages.Count, eShopNotifications.Count, entries);
    }

    private async Task NotifyAllNumbersAsync(string buyerId, int orderId, NotificationKind kind, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        if (numbers.Count == 0)
        {
            // A shopper with no number on file is simply not messaged.
            _logger.LogInformation("Order {OrderId}: no contact number on file; {Kind} message not sent.", orderId, kind);
            return;
        }

        foreach (var number in numbers)
        {
            await SendOneAsync(orderId, buyerId, kind, number.PhoneNumber, cancellationToken);
        }
    }

    private async Task SendOneAsync(int orderId, string buyerId, NotificationKind kind, string toNumber, CancellationToken cancellationToken)
    {
        // Local record first, provider call second, completed after — so a send failure never strands state.
        var notification = new OrderNotification(orderId, buyerId, kind, toNumber);
        await _notificationRepository.AddAsync(notification, cancellationToken);

        try
        {
            var result = await _smsSender.SendAsync(toNumber, BuildBody(kind, orderId), cancellationToken);
            notification.RecordSent(result.Sid, result.Status, result.SentAtUtc, result.ErrorCode, result.ErrorMessage);
        }
        catch (SmsGatewayException ex)
        {
            // Never fail the underlying order operation because a message could not be sent.
            notification.RecordSendFailure("send_failed", ex.Message);
            _logger.LogWarning("Order {OrderId}: {Kind} message could not be sent (notification {NotificationId}).",
                orderId, kind, notification.Id);
        }

        await _notificationRepository.UpdateAsync(notification, cancellationToken);
    }

    private async Task ScheduleFollowUpsAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);

        foreach (var number in numbers)
        {
            var notification = new OrderNotification(orderId, buyerId, NotificationKind.DeliveryFollowUp, number.PhoneNumber, isScheduled: true);
            await _notificationRepository.AddAsync(notification, cancellationToken);

            try
            {
                var result = await _smsSender.ScheduleAsync(number.PhoneNumber, BuildBody(NotificationKind.DeliveryFollowUp, orderId), sendAt, cancellationToken);
                notification.RecordSent(result.Sid, result.Status, result.SentAtUtc, result.ErrorCode, result.ErrorMessage);
            }
            catch (SmsGatewayException ex)
            {
                notification.RecordSendFailure("schedule_failed", ex.Message);
                _logger.LogWarning("Order {OrderId}: delivery follow-up could not be scheduled (notification {NotificationId}).",
                    orderId, notification.Id);
            }

            await _notificationRepository.UpdateAsync(notification, cancellationToken);
        }
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByOrderSpecification(orderId), cancellationToken);
        foreach (var notification in notifications.Where(IsCancellableFollowUp))
        {
            try
            {
                var result = await _smsSender.CancelScheduledAsync(notification.MessageSid!, cancellationToken);
                notification.UpdateDeliveryOutcome(result.Status ?? "canceled", result.ErrorCode, result.ErrorMessage);
            }
            catch (SmsGatewayException)
            {
                // Best-effort: if the provider will not cancel it (e.g. it has already left), keep the last-known state.
                _logger.LogWarning("Order {OrderId}: could not cancel follow-up notification {NotificationId}.",
                    orderId, notification.Id);
            }

            await _notificationRepository.UpdateAsync(notification, cancellationToken);
        }
    }

    private static bool IsCancellableFollowUp(OrderNotification n) =>
        n.Kind == NotificationKind.DeliveryFollowUp
        && n.IsScheduled
        && !string.IsNullOrEmpty(n.MessageSid)
        && !(n.ProviderStatus is not null && TerminalStatuses.Contains(n.ProviderStatus));

    private async Task<IReadOnlyList<OrderNotification>> GetAndRefreshNotificationsAsync(int orderId, CancellationToken cancellationToken)
    {
        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByOrderSpecification(orderId), cancellationToken);

        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.MessageSid)
                || notification.ContentRedacted
                || (notification.ProviderStatus is not null && TerminalStatuses.Contains(notification.ProviderStatus)))
            {
                continue;
            }

            try
            {
                var result = await _smsSender.GetStatusAsync(notification.MessageSid, cancellationToken);
                notification.UpdateDeliveryOutcome(result.Status, result.ErrorCode, result.ErrorMessage);
                await _notificationRepository.UpdateAsync(notification, cancellationToken);
            }
            catch (SmsGatewayException)
            {
                // Best-effort refresh — keep the last-known outcome if the provider read fails.
            }
        }

        return notifications.OrderBy(n => n.Id).ToList();
    }

    private async Task<OrderSummary> BuildOrderSummaryAsync(Order order, CancellationToken cancellationToken)
    {
        var notifications = await GetAndRefreshNotificationsAsync(order.Id, cancellationToken);
        return new OrderSummary(
            order.Id,
            order.Status.ToString(),
            order.OrderDate,
            order.Total(),
            notifications.Select(ToSummary).ToList());
    }

    private static NotificationSummary ToSummary(OrderNotification n) =>
        new(
            n.Id,
            n.Kind.ToString(),
            n.ProviderStatus,
            n.MessageSid,
            n.IsScheduled,
            n.ContentRedacted,
            n.ProviderErrorCode,
            n.ProviderErrorMessage,
            n.CreatedAtUtc,
            n.SentAtUtc);

    private static string BuildBody(NotificationKind kind, int orderId) => kind switch
    {
        NotificationKind.OrderPlaced => $"eShopOnWeb: your order #{orderId} has been placed. Thank you for shopping with us!",
        NotificationKind.Dispatched => $"eShopOnWeb: good news - your order #{orderId} is on its way!",
        NotificationKind.DeliveryFollowUp => $"eShopOnWeb: how did the delivery of order #{orderId} go? We'd love your feedback.",
        NotificationKind.Cancelled => $"eShopOnWeb: your order #{orderId} has been cancelled. Please contact us with any questions.",
        _ => $"eShopOnWeb: an update about your order #{orderId}."
    };
}
