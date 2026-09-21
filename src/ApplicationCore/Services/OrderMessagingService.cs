using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderMessagingService : IOrderMessagingService
{
    // "A few days later" for the delivery follow-up (within the provider's scheduling window).
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    // Provider statuses that mean the message has left / reached its final state.
    private static readonly HashSet<string> TerminalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "sent", "delivered", "undelivered", "failed", "canceled",
        "receiving", "received", "read", "partially_delivered"
    };

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IReadRepository<ContactNumber> _contactNumberRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly ISmsGateway _smsGateway;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderMessagingService> _logger;

    public OrderMessagingService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IReadRepository<ContactNumber> contactNumberRepository,
        IRepository<OrderNotification> notificationRepository,
        ISmsGateway smsGateway,
        IUriComposer uriComposer,
        IAppLogger<OrderMessagingService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _contactNumberRepository = contactNumberRepository;
        _notificationRepository = notificationRepository;
        _smsGateway = smsGateway;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> lines, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
            throw new ArgumentException("An order must contain at least one line.", nameof(lines));
        if (lines.Any(l => l.Quantity <= 0))
            throw new ArgumentException("Every order line must have a quantity of at least 1.", nameof(lines));

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), ct);

        var items = new List<OrderItem>();
        foreach (var line in lines)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw new ArgumentException($"Catalog item {line.CatalogItemId} does not exist.", nameof(lines));
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            items.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        // Reuse the existing Order/OrderItem model. Shipping address is out of scope for SMS.
        var shippingAddress = new Address("N/A", "N/A", "N/A", "N/A", "N/A");
        var order = new Order(buyerId, shippingAddress, items);
        order = await _orderRepository.AddAsync(order, ct);
        _logger.LogInformation($"Placed order {order.Id} for {buyerId} ({items.Count} line(s)).");

        // Tell the shopper — failure must not fail the placement.
        var toNumber = await ResolveNumberAsync(buyerId, ct);
        if (toNumber is not null)
            await SendAndRecordAsync(order.Id, buyerId, toNumber, NotificationType.OrderPlaced, ct);

        return order.Id;
    }

    public async Task<bool> DispatchOrderAsync(int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, ct);
        if (order is null)
            return false;

        var toNumber = await ResolveNumberAsync(order.BuyerId, ct);
        if (toNumber is not null)
        {
            // Tell them it's on its way (now)...
            await SendAndRecordAsync(orderId, order.BuyerId, toNumber, NotificationType.OrderDispatched, ct);
            // ...and queue the "how did delivery go?" follow-up WITH THE PROVIDER for a few days later.
            await ScheduleFollowUpAsync(orderId, order.BuyerId, toNumber, ct);
        }
        _logger.LogInformation($"Dispatched order {orderId}.");
        return true;
    }

    public async Task<bool> CancelOrderAsync(int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, ct);
        if (order is null)
            return false;

        // Call off any not-yet-sent follow-up so it can never reach the shopper.
        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByOrderSpecification(orderId), ct);
        foreach (var n in notifications.Where(n => n.IsScheduledFollowUp && n.MessageSid is not null && !TerminalStatuses.Contains(n.Status)))
        {
            try
            {
                var result = await _smsGateway.CancelScheduledAsync(n.MessageSid!, ct);
                n.UpdateStatus(result.Status, result.ErrorCode, result.ErrorMessage, result.DateSent);
                await _notificationRepository.UpdateAsync(n, ct);
                _logger.LogInformation($"Cancelled scheduled follow-up notification {n.Id} for order {orderId}.");
            }
            catch (SmsGatewayException ex)
            {
                // Do not fail the cancel operation, but make the miss loud and durable.
                n.MarkCancelFailed();
                await _notificationRepository.UpdateAsync(n, ct);
                _logger.LogWarning($"FAILED to cancel scheduled follow-up notification {n.Id} for order {orderId}: {ex.Message}. It may still send; reconcile.");
            }
        }

        var toNumber = await ResolveNumberAsync(order.BuyerId, ct);
        if (toNumber is not null)
            await SendAndRecordAsync(orderId, order.BuyerId, toNumber, NotificationType.OrderCancelled, ct);

        _logger.LogInformation($"Cancelled order {orderId}.");
        return true;
    }

    public async Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        // Idempotency: a repeat under the same key returns the earlier result without sending again.
        var duplicate = await _notificationRepository.FirstOrDefaultAsync(
            new OrderNotificationByIdempotencyKeySpecification(idempotencyKey), ct);
        if (duplicate is not null)
        {
            _logger.LogInformation($"Resend idempotency key already used; returning notification {duplicate.Id} without sending.");
            return new ResendResult(true, duplicate.Id, true);
        }

        var original = await _notificationRepository.GetByIdAsync(notificationId, ct);
        if (original is null)
            return new ResendResult(false, 0, false);

        var resend = new OrderNotification(original.OrderId, original.BuyerId, original.Type, original.ToPhoneNumber,
            idempotencyKey: idempotencyKey);
        var body = NotificationMessageBuilder.Build(original.Type, original.OrderId);
        try
        {
            var sent = await _smsGateway.SendAsync(original.ToPhoneNumber, body, ct);
            resend.RecordSent(sent.Sid, sent.Status, sent.ErrorCode, sent.ErrorMessage, sent.DateSent);
        }
        catch (SmsGatewayException ex)
        {
            resend.RecordSendFailure(ex.Message);
            _logger.LogWarning($"Resend of notification {notificationId} failed: {ex.Message}");
        }

        resend = await _notificationRepository.AddAsync(resend, ct);
        _logger.LogInformation($"Resend produced notification {resend.Id} from {notificationId}.");
        return new ResendResult(true, resend.Id, false);
    }

    public async Task<bool> RedactContentAsync(int notificationId, CancellationToken ct)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, ct);
        if (notification is null)
            return false;

        // Dispose of the provider's copy of the content; the fact of the send survives.
        if (!string.IsNullOrEmpty(notification.MessageSid))
            await _smsGateway.RedactContentAsync(notification.MessageSid!, ct); // throws on failure -> operator sees it

        notification.MarkContentRedacted();
        await _notificationRepository.UpdateAsync(notification, ct);
        _logger.LogInformation($"Disposed of provider content for notification {notificationId}.");
        return true;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var providerMessages = await _smsGateway.ListSentFromConfiguredNumberAsync(from, to, ct);

        var allNotifications = await _notificationRepository.ListAsync(ct);
        var eShopBySid = allNotifications
            .Where(n => !string.IsNullOrEmpty(n.MessageSid))
            .GroupBy(n => n.MessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var providerSids = new HashSet<string>(providerMessages.Select(m => m.Sid));

        var entries = new List<ReconciliationEntry>();
        var matched = 0;
        foreach (var m in providerMessages)
        {
            if (eShopBySid.ContainsKey(m.Sid))
            {
                matched++;
                entries.Add(new ReconciliationEntry(m.Sid, m.Status, m.To, "matched"));
            }
            else
            {
                entries.Add(new ReconciliationEntry(m.Sid, m.Status, m.To, "provider_only"));
            }
        }

        // eShop believes it sent these within the range, but the provider did not return them.
        var eShopInRange = allNotifications
            .Where(n => !string.IsNullOrEmpty(n.MessageSid))
            .Where(n => WithinRange(n.ProviderDateSent ?? n.CreatedDate, from, to))
            .ToList();
        foreach (var n in eShopInRange)
        {
            if (!providerSids.Contains(n.MessageSid!))
                entries.Add(new ReconciliationEntry(n.MessageSid!, n.Status, n.ToPhoneNumber, "eshop_only"));
        }

        return new ReconciliationReport(from, to, providerMessages.Count, eShopInRange.Count, matched, entries);
    }

    public async Task<IReadOnlyList<OrderNotification>?> GetOrderNotificationsAsync(int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, ct);
        if (order is null)
            return null;

        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByOrderSpecification(orderId), ct);
        await RefreshStatusesAsync(notifications, ct);
        return notifications;
    }

    public async Task RefreshBuyerNotificationsAsync(string buyerId, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByBuyerSpecification(buyerId), ct);
        await RefreshStatusesAsync(notifications, ct);
    }

    // ---- helpers -------------------------------------------------------------------------------

    private async Task<string?> ResolveNumberAsync(string buyerId, CancellationToken ct)
    {
        var numbers = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), ct);
        return numbers.Count > 0 ? numbers[0].PhoneNumber : null;
    }

    private async Task SendAndRecordAsync(int orderId, string buyerId, string toNumber, NotificationType type, CancellationToken ct)
    {
        var notification = new OrderNotification(orderId, buyerId, type, toNumber);
        var body = NotificationMessageBuilder.Build(type, orderId);
        try
        {
            var sent = await _smsGateway.SendAsync(toNumber, body, ct);
            notification.RecordSent(sent.Sid, sent.Status, sent.ErrorCode, sent.ErrorMessage, sent.DateSent);
            _logger.LogInformation($"Sent {type} notification for order {orderId} (sid {sent.Sid}, status {sent.Status}).");
        }
        catch (SmsGatewayException ex)
        {
            notification.RecordSendFailure(ex.Message);
            _logger.LogWarning($"Send of {type} notification for order {orderId} failed: {ex.Message}");
        }
        await _notificationRepository.AddAsync(notification, ct);
    }

    private async Task ScheduleFollowUpAsync(int orderId, string buyerId, string toNumber, CancellationToken ct)
    {
        var notification = new OrderNotification(orderId, buyerId, NotificationType.DeliveryFollowUp, toNumber, isScheduledFollowUp: true);
        var body = NotificationMessageBuilder.Build(NotificationType.DeliveryFollowUp, orderId);
        try
        {
            var sent = await _smsGateway.ScheduleAsync(toNumber, body, DateTimeOffset.UtcNow.Add(FollowUpDelay), ct);
            notification.RecordSent(sent.Sid, sent.Status, sent.ErrorCode, sent.ErrorMessage, sent.DateSent);
            _logger.LogInformation($"Scheduled follow-up for order {orderId} (sid {sent.Sid}, status {sent.Status}).");
        }
        catch (SmsGatewayException ex)
        {
            notification.RecordSendFailure(ex.Message);
            _logger.LogWarning($"Scheduling follow-up for order {orderId} failed: {ex.Message}");
        }
        await _notificationRepository.AddAsync(notification, ct);
    }

    private async Task RefreshStatusesAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken ct)
    {
        foreach (var n in notifications)
        {
            if (string.IsNullOrEmpty(n.MessageSid) || TerminalStatuses.Contains(n.Status))
                continue;
            try
            {
                var latest = await _smsGateway.FetchAsync(n.MessageSid!, ct);
                n.UpdateStatus(latest.Status, latest.ErrorCode, latest.ErrorMessage, latest.DateSent);
                await _notificationRepository.UpdateAsync(n, ct);
            }
            catch (SmsGatewayException ex)
            {
                // Best-effort refresh; a stale status is better than a failed read.
                _logger.LogWarning($"Could not refresh status for notification {n.Id}: {ex.Message}");
            }
        }
    }

    private static bool WithinRange(DateTimeOffset value, DateTimeOffset from, DateTimeOffset to) =>
        value >= from && value <= to;
}
