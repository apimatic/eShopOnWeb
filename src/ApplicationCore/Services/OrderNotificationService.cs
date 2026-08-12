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
using NotFoundException = Microsoft.eShopWeb.ApplicationCore.Exceptions.NotFoundException;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    // A few days out — kept comfortably inside the provider's scheduling window; the exact bound is
    // provider-enforced at send time, so we do not assert a specific number here.
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    // Delivery outcomes that will not change again — no point re-asking the provider about them.
    private static readonly HashSet<string> TerminalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "delivered", "undelivered", "failed", "received", "read", NotificationStatuses.Canceled
    };

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _catalogItemRepository;
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IUriComposer _uriComposer;
    private readonly ISmsGateway _smsGateway;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> catalogItemRepository,
        IRepository<ContactNumber> contactNumberRepository,
        IRepository<OrderNotification> notificationRepository,
        IUriComposer uriComposer,
        ISmsGateway smsGateway,
        IAppLogger<OrderNotificationService> logger)
    {
        _orderRepository = orderRepository;
        _catalogItemRepository = catalogItemRepository;
        _contactNumberRepository = contactNumberRepository;
        _notificationRepository = notificationRepository;
        _uriComposer = uriComposer;
        _smsGateway = smsGateway;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines, Address? shipToAddress, CancellationToken ct = default)
    {
        Guard.Against.NullOrWhiteSpace(buyerId, nameof(buyerId));
        Guard.Against.Null(lines, nameof(lines));
        if (lines.Count == 0)
        {
            throw new ConflictException("An order must contain at least one line.");
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItemRepository.ListAsync(new CatalogItemsSpecification(ids), ct);

        var orderItems = new List<OrderItem>();
        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new ConflictException($"Quantity for catalog item {line.CatalogItemId} must be greater than zero.");
            }

            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId);
            if (catalogItem is null)
            {
                throw new NotFoundException($"Catalog item {line.CatalogItemId} was not found.");
            }

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var address = shipToAddress ?? DefaultAddress();
        var order = new Order(buyerId, address, orderItems);
        order = await _orderRepository.AddAsync(order, ct);

        await NotifyAsync(order.Id, buyerId, NotificationKind.OrderPlaced,
            $"eShopOnWeb: thanks! Your order #{order.Id} has been placed.", ct);

        return order;
    }

    public async Task DispatchAsync(int orderId, CancellationToken ct = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, ct);
        if (order is null)
        {
            throw new NotFoundException($"Order {orderId} was not found.");
        }

        // State transition first; if it is invalid we reject before anything is sent.
        order.MarkDispatched();
        await _orderRepository.UpdateAsync(order, ct);

        await NotifyAsync(order.Id, order.BuyerId, NotificationKind.OrderDispatched,
            $"eShopOnWeb: good news — your order #{order.Id} is on its way!", ct);

        // Queue the "how did delivery go?" follow-up WITH THE PROVIDER for a few days out.
        await ScheduleFollowUpAsync(order.Id, order.BuyerId,
            $"eShopOnWeb: how did the delivery of your order #{order.Id} go? We'd love your feedback.",
            DateTimeOffset.UtcNow.Add(FollowUpDelay), ct);
    }

    public async Task CancelAsync(int orderId, CancellationToken ct = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, ct);
        if (order is null)
        {
            throw new NotFoundException($"Order {orderId} was not found.");
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, ct);

        // Call off any queued follow-up BEFORE it goes out — a "how did delivery go?" message for a
        // cancelled order is exactly the incident this prevents.
        await CancelScheduledFollowUpsAsync(order.Id, ct);

        await NotifyAsync(order.Id, order.BuyerId, NotificationKind.OrderCancelled,
            $"eShopOnWeb: your order #{order.Id} has been cancelled.", ct);
    }

    public async Task<IReadOnlyList<OrderWithNotifications>> GetMyOrdersAsync(string buyerId, CancellationToken ct = default)
    {
        Guard.Against.NullOrWhiteSpace(buyerId, nameof(buyerId));

        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);
        var notifications = await _notificationRepository.ListAsync(new NotificationsByBuyerSpecification(buyerId), ct);
        await RefreshDeliveryStatesAsync(notifications, ct);

        var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => (IReadOnlyList<OrderNotification>)g.ToList());
        return orders
            .Select(o => new OrderWithNotifications(o,
                byOrder.TryGetValue(o.Id, out var list) ? list : Array.Empty<OrderNotification>()))
            .ToList();
    }

    public async Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(int orderId, string buyerId, CancellationToken ct = default)
    {
        Guard.Against.NullOrWhiteSpace(buyerId, nameof(buyerId));

        var order = await _orderRepository.GetByIdAsync(orderId, ct);
        if (order is null || order.BuyerId != buyerId)
        {
            // Not found OR not the caller's — reported the same way so another shopper's order is never revealed.
            throw new NotFoundException($"Order {orderId} was not found.");
        }

        var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderSpecification(orderId), ct);
        await RefreshDeliveryStatesAsync(notifications, ct);
        return notifications;
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct = default)
    {
        Guard.Against.NullOrWhiteSpace(idempotencyKey, nameof(idempotencyKey));

        var original = await _notificationRepository.GetByIdAsync(notificationId, ct);
        if (original is null)
        {
            throw new NotFoundException($"Notification {notificationId} was not found.");
        }

        // Idempotency: a repeat under the same key must not send a second message. If we already have a
        // notification stamped with this key, return it unchanged.
        var existing = await _notificationRepository
            .FirstOrDefaultAsync(new NotificationByIdempotencyKeySpecification(idempotencyKey), ct);
        if (existing is not null)
        {
            return existing;
        }

        if (original.ContentRedacted || string.IsNullOrEmpty(original.Body))
        {
            throw new ConflictException("The message content has been disposed of and cannot be re-sent.");
        }

        var resend = new OrderNotification(original.OrderId, original.BuyerId, original.Kind, original.ToNumber, original.Body!);
        resend.SetIdempotencyKey(idempotencyKey);

        var result = await _smsGateway.SendAsync(original.ToNumber, original.Body!, ct);
        resend.RecordSendOutcome(
            result.MessageSid,
            result.Accepted ? result.Status : NotificationStatuses.SendFailed,
            result.ErrorCode,
            result.FailureReason);

        return await _notificationRepository.AddAsync(resend, ct);
    }

    public async Task RedactNotificationContentAsync(int notificationId, CancellationToken ct = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, ct);
        if (notification is null)
        {
            throw new NotFoundException($"Notification {notificationId} was not found.");
        }

        if (notification.ContentRedacted)
        {
            return; // already disposed of
        }

        // Dispose of the text at the provider first (throws if it cannot), then locally — so it is no
        // longer retrievable from the provider either, while the sent-fact and outcome survive.
        if (!string.IsNullOrEmpty(notification.MessageSid))
        {
            await _smsGateway.RedactContentAsync(notification.MessageSid!, ct);
        }

        notification.RedactContent();
        await _notificationRepository.UpdateAsync(notification, ct);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        // Ask the provider for ONLY the configured sending number's messages over the range.
        var providerRecords = await _smsGateway.ListSentFromConfiguredNumberAsync(from, to, ct);
        var localRecords = await _notificationRepository.ListAsync(new SentNotificationsInRangeSpecification(from, to), ct);

        var localBySid = localRecords
            .Where(n => !string.IsNullOrEmpty(n.MessageSid))
            .GroupBy(n => n.MessageSid!)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var providerBySid = providerRecords
            .GroupBy(p => p.Sid)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var matched = new List<ReconciliationMatch>();
        var inEShopOnly = new List<ReconciliationEShopEntry>();
        foreach (var local in localBySid.Values)
        {
            if (providerBySid.TryGetValue(local.MessageSid!, out var provider))
            {
                matched.Add(new ReconciliationMatch(local.Id, local.OrderId, local.MessageSid!, local.Status, provider.Status, provider.ErrorCode));
            }
            else
            {
                inEShopOnly.Add(new ReconciliationEShopEntry(local.Id, local.OrderId, local.MessageSid!, local.Status));
            }
        }

        var inProviderOnly = providerBySid.Values
            .Where(p => !localBySid.ContainsKey(p.Sid))
            .Select(p => new ReconciliationProviderEntry(p.Sid, p.Status, p.ErrorCode, p.DateSent))
            .ToList();

        return new ReconciliationReport(from, to, _smsGateway.ConfiguredFromNumber, matched, inEShopOnly, inProviderOnly);
    }

    // --- helpers -----------------------------------------------------------------------------

    private async Task NotifyAsync(int orderId, string buyerId, NotificationKind kind, string body, CancellationToken ct)
    {
        // A message that cannot be sent must never fail the underlying order operation.
        try
        {
            var numbers = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), ct);
            foreach (var number in numbers)
            {
                var notification = new OrderNotification(orderId, buyerId, kind, number.PhoneNumber, body);
                var result = await _smsGateway.SendAsync(number.PhoneNumber, body, ct);
                notification.RecordSendOutcome(
                    result.MessageSid,
                    result.Accepted ? result.Status : NotificationStatuses.SendFailed,
                    result.ErrorCode,
                    result.FailureReason);
                await _notificationRepository.AddAsync(notification, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Order {orderId}: notification '{kind}' could not be dispatched: {ex.Message}");
        }
    }

    private async Task ScheduleFollowUpAsync(int orderId, string buyerId, string body, DateTimeOffset sendAt, CancellationToken ct)
    {
        try
        {
            var numbers = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), ct);
            foreach (var number in numbers)
            {
                var notification = new OrderNotification(orderId, buyerId, NotificationKind.DeliveryFollowUp, number.PhoneNumber, body);
                var result = await _smsGateway.ScheduleAsync(number.PhoneNumber, body, sendAt, ct);
                notification.RecordScheduled(
                    result.MessageSid,
                    result.Accepted ? result.Status : NotificationStatuses.SendFailed,
                    sendAt,
                    result.FailureReason);
                await _notificationRepository.AddAsync(notification, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Order {orderId}: delivery follow-up could not be scheduled: {ex.Message}");
        }
    }

    private async Task CancelScheduledFollowUpsAsync(int orderId, CancellationToken ct)
    {
        try
        {
            var followUps = await _notificationRepository.ListAsync(new ScheduledFollowUpsByOrderSpecification(orderId), ct);
            foreach (var followUp in followUps)
            {
                var result = await _smsGateway.CancelScheduledAsync(followUp.MessageSid!, ct);
                followUp.MarkScheduledCancelled(result.Canceled ? null : result.FailureReason);
                await _notificationRepository.UpdateAsync(followUp, ct);
                if (!result.Canceled)
                {
                    _logger.LogWarning($"Order {orderId}: a queued follow-up could not be cancelled with the provider: {result.FailureReason}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Order {orderId}: cancelling queued follow-ups failed: {ex.Message}");
        }
    }

    private async Task RefreshDeliveryStatesAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken ct)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.MessageSid) || TerminalStatuses.Contains(notification.Status))
            {
                continue;
            }

            var outcome = await _smsGateway.FetchStatusAsync(notification.MessageSid!, ct);
            if (outcome is null || string.Equals(outcome.Status, notification.Status, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            notification.SyncDeliveryState(outcome.Status, outcome.ErrorCode);
            await _notificationRepository.UpdateAsync(notification, ct);
        }
    }

    private static Address DefaultAddress()
        => new Address("123 Main St.", "Kent", "OH", "United States", "44240");
}
