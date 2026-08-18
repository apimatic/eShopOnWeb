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
    // The follow-up asking how delivery went goes out a few days after dispatch. It is queued with the
    // provider (SendAt), not held in this application.
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    // Provider delivery outcomes that are final — no point re-reading them from the provider.
    private static readonly HashSet<string> TerminalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "delivered", "undelivered", "failed", "canceled"
    };

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly ISmsGateway _smsGateway;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<ContactNumber> contactNumberRepository,
        IRepository<OrderNotification> notificationRepository,
        ISmsGateway smsGateway,
        IUriComposer uriComposer,
        IAppLogger<OrderNotificationService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _contactNumberRepository = contactNumberRepository;
        _notificationRepository = notificationRepository;
        _smsGateway = smsGateway;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<Order?> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines, Address shipToAddress, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(shipToAddress, nameof(shipToAddress));
        if (lines is null || lines.Count == 0)
        {
            return null;
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), ct);

        var orderItems = new List<OrderItem>();
        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                return null;
            }
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId);
            if (catalogItem is null)
            {
                return null; // an unknown catalog item id — caller error
            }
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, shipToAddress, orderItems);
        await _orderRepository.AddAsync(order, ct);

        _logger.LogInformation("Order {OrderId} placed for buyer via PublicApi.", order.Id);

        await SendImmediateAsync(order, NotificationKind.OrderPlaced,
            $"eShop: your order #{order.Id} has been placed. Thank you for shopping with us!", ct);

        return order;
    }

    public async Task<OrderTransition> DispatchAsync(int orderId, CancellationToken ct = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, ct);
        if (order is null)
        {
            return new OrderTransition(OrderTransitionOutcome.OrderNotFound, null);
        }

        var existing = await _notificationRepository.ListAsync(new OrderNotificationsByOrderSpecification(orderId), ct);
        if (existing.Any(n => n.Kind == NotificationKind.OrderCancelled))
        {
            return new OrderTransition(OrderTransitionOutcome.AlreadyCancelled, order);
        }
        if (existing.Any(n => n.Kind == NotificationKind.OrderDispatched))
        {
            return new OrderTransition(OrderTransitionOutcome.AlreadyDispatched, order);
        }

        var numbers = await _contactNumberRepository.ListAsync(new ContactNumbersByOwnerSpecification(order.BuyerId), ct);

        foreach (var number in numbers)
        {
            // Tell the shopper it is on its way.
            await SendOneAsync(order.Id, order.BuyerId, NotificationKind.OrderDispatched, number.E164Number,
                $"eShop: good news! Your order #{order.Id} is on its way.", ct);

            // Queue the "how did delivery go?" follow-up with the provider for a few days later.
            await ScheduleOneAsync(order.Id, order.BuyerId, NotificationKind.DeliveryFollowUp, number.E164Number,
                $"eShop: how did the delivery of your order #{order.Id} go? We'd love your feedback.",
                DateTimeOffset.UtcNow.Add(FollowUpDelay), ct);
        }

        _logger.LogInformation("Order {OrderId} dispatched; {Count} contact number(s) notified.", order.Id, numbers.Count);
        return new OrderTransition(OrderTransitionOutcome.Success, order);
    }

    public async Task<OrderTransition> CancelAsync(int orderId, CancellationToken ct = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, ct);
        if (order is null)
        {
            return new OrderTransition(OrderTransitionOutcome.OrderNotFound, null);
        }

        var existing = await _notificationRepository.ListAsync(new OrderNotificationsByOrderSpecification(orderId), ct);
        if (existing.Any(n => n.Kind == NotificationKind.OrderCancelled))
        {
            return new OrderTransition(OrderTransitionOutcome.AlreadyCancelled, order);
        }

        // Call off any follow-up that has not yet gone out, so asking how delivery went can never reach a
        // shopper whose order was cancelled. Keyed by the provider SID, independent of contact numbers.
        foreach (var pending in existing.Where(n => n.IsPendingScheduled && n.ProviderSid is not null))
        {
            try
            {
                await _smsGateway.CancelScheduledAsync(pending.ProviderSid!, ct);
                pending.MarkCanceled();
                await _notificationRepository.UpdateAsync(pending, ct);
                _logger.LogInformation("Scheduled follow-up notification {NotificationId} for order {OrderId} called off.", pending.Id, order.Id);
            }
            catch (SmsGatewayException ex)
            {
                // The cancel operation itself must still succeed. Record that the call-off could not be
                // completed so an operator can act on it.
                pending.UpdateDeliveryState(pending.ProviderStatus, pending.ProviderErrorCode, "Follow-up cancellation could not be completed at the provider.");
                await _notificationRepository.UpdateAsync(pending, ct);
                _logger.LogWarning("Could not call off follow-up notification {NotificationId} for order {OrderId}: provider status {Status}.",
                    pending.Id, order.Id, ex.StatusCode?.ToString() ?? "unreachable");
            }
        }

        var numbers = await _contactNumberRepository.ListAsync(new ContactNumbersByOwnerSpecification(order.BuyerId), ct);
        foreach (var number in numbers)
        {
            await SendOneAsync(order.Id, order.BuyerId, NotificationKind.OrderCancelled, number.E164Number,
                $"eShop: your order #{order.Id} has been cancelled. If this is unexpected, please contact support.", ct);
        }

        _logger.LogInformation("Order {OrderId} cancelled; {Count} contact number(s) notified.", order.Id, numbers.Count);
        return new OrderTransition(OrderTransitionOutcome.Success, order);
    }

    public async Task<IReadOnlyList<OrderWithNotifications>> GetMyOrdersAsync(string ownerId, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));

        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(ownerId), ct);
        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByOwnerSpecification(ownerId), ct);

        await RefreshDeliveryStateAsync(notifications, ct);

        var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => (IReadOnlyList<OrderNotification>)g.ToList());

        return orders
            .Select(o => new OrderWithNotifications(o, byOrder.TryGetValue(o.Id, out var ns) ? ns : Array.Empty<OrderNotification>()))
            .ToList();
    }

    public async Task<IReadOnlyList<OrderNotification>?> GetOrderNotificationsAsync(string ownerId, int orderId, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));

        var order = await _orderRepository.GetByIdAsync(orderId, ct);
        if (order is null || order.BuyerId != ownerId)
        {
            return null; // not the caller's order (or does not exist) — one shopper never sees another's
        }

        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByOrderSpecification(orderId), ct);
        await RefreshDeliveryStateAsync(notifications, ct);
        return notifications;
    }

    public async Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        // Idempotency: a repeat under the same key returns the notification the first attempt produced,
        // without sending again.
        var priorForKey = await _notificationRepository.FirstOrDefaultAsync(new OrderNotificationByResendKeySpecification(idempotencyKey), ct);
        if (priorForKey is not null)
        {
            return new ResendResult(ResendOutcome.AlreadyProcessed, priorForKey);
        }

        var original = await _notificationRepository.GetByIdAsync(notificationId, ct);
        if (original is null)
        {
            return new ResendResult(ResendOutcome.NotificationNotFound, null);
        }

        if (original.ContentRedacted || string.IsNullOrEmpty(original.Body))
        {
            // The content has been disposed of — there is nothing to re-send.
            return new ResendResult(ResendOutcome.ContentDisposed, null);
        }

        // The destination must still be on file: after a number is removed, nothing may be sent to it again.
        var numbers = await _contactNumberRepository.ListAsync(new ContactNumbersByOwnerSpecification(original.OwnerId), ct);
        if (numbers.All(n => n.E164Number != original.ToNumber))
        {
            return new ResendResult(ResendOutcome.DestinationRemoved, null);
        }

        var resend = new OrderNotification(original.OrderId, original.OwnerId, original.Kind, original.ToNumber, original.Body!);
        resend.AssignResendKey(idempotencyKey);
        try
        {
            var sent = await _smsGateway.SendAsync(original.ToNumber, original.Body!, ct);
            if (string.IsNullOrEmpty(sent.ProviderSid))
            {
                resend.RecordSendFailure("The provider accepted no message identifier.");
            }
            else
            {
                resend.RecordAccepted(sent.ProviderSid!, sent.Status, sent.ErrorCode, sent.ErrorMessage);
            }
        }
        catch (SmsGatewayException ex)
        {
            // Record the attempt (with its key) even on failure, so a repeat under the same key does not
            // send again; a genuine retry uses a fresh key.
            resend.RecordSendFailure(DescribeProviderFailure(ex));
        }

        await _notificationRepository.AddAsync(resend, ct);
        _logger.LogInformation("Resend produced notification {NotificationId} for order {OrderId}.", resend.Id, resend.OrderId);
        return new ResendResult(ResendOutcome.Sent, resend);
    }

    public async Task<bool> DisposeContentAsync(int notificationId, CancellationToken ct = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, ct);
        if (notification is null)
        {
            return false;
        }

        // Dispose of the content at the provider first, so its text is no longer retrievable there. If the
        // provider cannot complete this, surface it rather than clearing locally and claiming success.
        if (!string.IsNullOrEmpty(notification.ProviderSid))
        {
            await _smsGateway.RedactContentAsync(notification.ProviderSid!, ct);
        }

        notification.RedactContent();
        await _notificationRepository.UpdateAsync(notification, ct);
        _logger.LogInformation("Content disposed for notification {NotificationId} (order {OrderId}); record and status retained.",
            notification.Id, notification.OrderId);
        return true;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        // Ask the provider for the configured sending number's messages over the range (it filters, not us).
        var providerMessages = await _smsGateway.ListSentMessagesAsync(from, to, ct);

        // eShop's own record of messages it believes it handed to the provider in the range.
        var eshopNotifications = await _notificationRepository.ListAsync(new OrderNotificationsSentBetweenSpecification(from, to), ct);
        var eshopBySid = eshopNotifications
            .Where(n => n.ProviderSid is not null)
            .GroupBy(n => n.ProviderSid!)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var providerSids = new HashSet<string>(providerMessages.Select(m => m.Sid), StringComparer.OrdinalIgnoreCase);

        var entries = new List<ReconciliationEntry>();

        foreach (var pm in providerMessages)
        {
            if (eshopBySid.TryGetValue(pm.Sid, out var known))
            {
                entries.Add(new ReconciliationEntry(pm.Sid, known.Id, known.OrderId, pm.Status, known.ProviderStatus, ReconciliationState.InSync));
            }
            else
            {
                entries.Add(new ReconciliationEntry(pm.Sid, null, null, pm.Status, null, ReconciliationState.ProviderOnly));
            }
        }

        foreach (var n in eshopBySid.Values)
        {
            if (!providerSids.Contains(n.ProviderSid!))
            {
                entries.Add(new ReconciliationEntry(n.ProviderSid, n.Id, n.OrderId, null, n.ProviderStatus, ReconciliationState.EShopOnly));
            }
        }

        var inSync = entries.Count(e => e.State == ReconciliationState.InSync);
        var providerOnly = entries.Count(e => e.State == ReconciliationState.ProviderOnly);
        var eshopOnly = entries.Count(e => e.State == ReconciliationState.EShopOnly);

        return new ReconciliationReport(
            from, to, _smsGateway.SendingNumber,
            providerMessages.Count, eshopBySid.Count,
            inSync, providerOnly, eshopOnly,
            entries);
    }

    // ---- helpers ---------------------------------------------------------------------------------------

    private async Task SendImmediateAsync(Order order, NotificationKind kind, string body, CancellationToken ct)
    {
        var numbers = await _contactNumberRepository.ListAsync(new ContactNumbersByOwnerSpecification(order.BuyerId), ct);
        // A shopper with no number on file is simply not messaged.
        foreach (var number in numbers)
        {
            await SendOneAsync(order.Id, order.BuyerId, kind, number.E164Number, body, ct);
        }
    }

    private async Task SendOneAsync(int orderId, string ownerId, NotificationKind kind, string toNumber, string body, CancellationToken ct)
    {
        var notification = new OrderNotification(orderId, ownerId, kind, toNumber, body);
        try
        {
            var sent = await _smsGateway.SendAsync(toNumber, body, ct);
            if (string.IsNullOrEmpty(sent.ProviderSid))
            {
                notification.RecordSendFailure("The provider accepted no message identifier.");
            }
            else
            {
                notification.RecordAccepted(sent.ProviderSid!, sent.Status, sent.ErrorCode, sent.ErrorMessage);
            }
        }
        catch (SmsGatewayException ex)
        {
            // A message that cannot be sent must never fail the underlying operation.
            notification.RecordSendFailure(DescribeProviderFailure(ex));
            _logger.LogWarning("Notification for order {OrderId} ({Kind}) could not be sent: {Reason}.", orderId, kind, notification.SendFailureReason ?? "unknown");
        }

        await _notificationRepository.AddAsync(notification, ct);
    }

    private async Task ScheduleOneAsync(int orderId, string ownerId, NotificationKind kind, string toNumber, string body, DateTimeOffset sendAt, CancellationToken ct)
    {
        var notification = new OrderNotification(orderId, ownerId, kind, toNumber, body);
        try
        {
            var scheduled = await _smsGateway.ScheduleAsync(toNumber, body, sendAt, ct);
            if (string.IsNullOrEmpty(scheduled.ProviderSid))
            {
                notification.RecordSendFailure("The provider accepted no message identifier.");
            }
            else
            {
                notification.RecordAccepted(scheduled.ProviderSid!, scheduled.Status, scheduled.ErrorCode, scheduled.ErrorMessage, sendAt);
            }
        }
        catch (SmsGatewayException ex)
        {
            notification.RecordSendFailure(DescribeProviderFailure(ex));
            _logger.LogWarning("Follow-up for order {OrderId} could not be scheduled: {Reason}.", orderId, notification.SendFailureReason ?? "unknown");
        }

        await _notificationRepository.AddAsync(notification, ct);
    }

    private async Task RefreshDeliveryStateAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken ct)
    {
        foreach (var n in notifications)
        {
            if (n.ProviderSid is null || n.SendFailed)
            {
                continue; // nothing at the provider to read
            }
            if (n.ProviderStatus is not null && TerminalStatuses.Contains(n.ProviderStatus))
            {
                continue; // already final
            }

            try
            {
                var state = await _smsGateway.FetchDeliveryStateAsync(n.ProviderSid, ct);
                n.UpdateDeliveryState(state.Status, state.ErrorCode, state.ErrorMessage);
                await _notificationRepository.UpdateAsync(n, ct);
            }
            catch (SmsGatewayException ex)
            {
                // Reading a status must not fail the report — keep the last-known value.
                _logger.LogWarning("Could not refresh delivery state for notification {NotificationId}: provider status {Status}.",
                    n.Id, ex.StatusCode?.ToString() ?? "unreachable");
            }
        }
    }

    private static string DescribeProviderFailure(SmsGatewayException ex)
    {
        return ex.StatusCode is null
            ? "The messaging provider could not be reached."
            : $"The messaging provider rejected the message (status {(int)ex.StatusCode}).";
    }
}
