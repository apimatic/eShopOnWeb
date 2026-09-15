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
    /// <summary>How long after dispatch the "how did the delivery go?" follow-up is queued for (within Twilio's 15 min–35 day window).</summary>
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<Order> _orders;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IReadRepository<CatalogItem> _catalog;
    private readonly IReadRepository<ContactNumber> _contactNumbers;
    private readonly IMessagingProvider _provider;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orders,
        IRepository<OrderNotification> notifications,
        IReadRepository<CatalogItem> catalog,
        IReadRepository<ContactNumber> contactNumbers,
        IMessagingProvider provider,
        IUriComposer uriComposer,
        IAppLogger<OrderNotificationService> logger)
    {
        _orders = orders;
        _notifications = notifications;
        _catalog = catalog;
        _contactNumbers = contactNumbers;
        _provider = provider;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    // ---------------------------------------------------------------- Shopper: place an order

    public async Task<int> PlaceOrderAsync(string ownerId, IReadOnlyList<OrderLine> lines, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));

        if (lines is null || lines.Count == 0)
            throw new OrderCreationException("An order must contain at least one item.");
        if (lines.Any(l => l.Quantity <= 0))
            throw new OrderCreationException("Every order line must have a quantity greater than zero.");

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalog.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);

        var items = new List<OrderItem>();
        foreach (var line in lines)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId);
            if (catalogItem is null)
                throw new OrderCreationException($"Catalog item {line.CatalogItemId} does not exist.");

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            items.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        // The PublicApi order surface captures no shipping address; the existing Order model requires one.
        var shipToAddress = new Address("N/A", "N/A", "N/A", "N/A", "N/A");
        var order = new Order(ownerId, shipToAddress, items);
        await _orders.AddAsync(order, cancellationToken);

        // Best-effort: a failed message must never fail the placement.
        await NotifyOwnerAsync(order, NotificationType.OrderPlaced, Bodies.Placed(order.Id), cancellationToken);

        return order.Id;
    }

    // ---------------------------------------------------------------- Operator: dispatch

    public async Task<bool> DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
            return false;

        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByOwnerSpecification(order.BuyerId), cancellationToken);
        foreach (var number in numbers)
        {
            // Tell the shopper it is on its way...
            await SendOneAsync(order, NotificationType.OrderDispatched, Bodies.Dispatched(order.Id), number.PhoneNumber, cancellationToken);

            // ...and queue the "how did it go?" follow-up with the provider for a few days later.
            await ScheduleFollowUpAsync(order, number.PhoneNumber, cancellationToken);
        }

        return true;
    }

    // ---------------------------------------------------------------- Operator: cancel

    public async Task<bool> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
            return false;

        // Call off any follow-up that has not yet gone out — a "how did delivery go?" for a cancelled
        // order is exactly the message this must prevent. Done independently of the cancel notice so
        // one failing never leaves the other undone.
        var scheduled = await _notifications.ListAsync(new ScheduledFollowUpsForOrderSpecification(orderId), cancellationToken);
        foreach (var followUp in scheduled)
        {
            try
            {
                await _provider.CancelScheduledAsync(followUp.ProviderMessageSid!, cancellationToken);
                followUp.MarkCanceled();
                await _notifications.UpdateAsync(followUp, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to cancel scheduled follow-up {NotificationId} for order {OrderId}: {Error}",
                    followUp.Id, orderId, Describe(ex));
            }
        }

        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByOwnerSpecification(order.BuyerId), cancellationToken);
        foreach (var number in numbers)
        {
            await SendOneAsync(order, NotificationType.OrderCanceled, Bodies.Canceled(order.Id), number.PhoneNumber, cancellationToken);
        }

        return true;
    }

    // ---------------------------------------------------------------- Shopper: read my orders

    public async Task<IReadOnlyList<OrderNotificationView>> GetMyOrdersAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(ownerId), cancellationToken);

        var views = new List<OrderNotificationView>();
        foreach (var order in orders)
        {
            var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderSpecification(order.Id), cancellationToken);
            await RefreshOutcomesAsync(notifications, cancellationToken);

            views.Add(new OrderNotificationView(
                order.Id,
                order.OrderDate,
                order.Total(),
                order.OrderItems.Select(i => new OrderLineView(i.ItemOrdered.CatalogItemId, i.ItemOrdered.ProductName, i.Units, i.UnitPrice)).ToList(),
                notifications.Select(ToView).ToList()));
        }

        return views;
    }

    // ---------------------------------------------------------------- Shopper: read one order's notifications

    public async Task<IReadOnlyList<NotificationView>?> GetOrderNotificationsAsync(string ownerId, int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);

        // Scope to the caller: an order belongs to the shopper who placed it. Unknown or not-yours → not found.
        if (order is null || order.BuyerId != ownerId)
            return null;

        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderSpecification(orderId), cancellationToken);
        await RefreshOutcomesAsync(notifications, cancellationToken);
        return notifications.Select(ToView).ToList();
    }

    // ---------------------------------------------------------------- Operator: resend

    public async Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return ResendResult.Failed("An idempotency key is required.");

        // Repeating a request under the same key must not send a second message.
        var priorForKey = await _notifications.FirstOrDefaultAsync(
            new OrderNotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (priorForKey is not null)
            return ResendResult.Duplicate(priorForKey.Id);

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (original is null)
            return ResendResult.NotFound();

        if (string.IsNullOrEmpty(original.Body))
            return ResendResult.Failed("The message content has been disposed of and cannot be resent.");

        // Record the resend (carrying the idempotency key) before contacting the provider, so a concurrent
        // repeat under the same key finds it and does not send again.
        var resend = new OrderNotification(original.OrderId, original.OwnerId, NotificationType.Resend, original.ToPhoneNumber, original.Body);
        resend.SetIdempotencyKey(idempotencyKey);
        await _notifications.AddAsync(resend, cancellationToken);

        try
        {
            var msg = await _provider.SendAsync(original.ToPhoneNumber, original.Body, cancellationToken);
            resend.MarkSubmitted(msg.Sid, msg.Status, msg.ErrorCode, msg.ErrorMessage, msg.DateSent);
        }
        catch (Exception ex)
        {
            resend.MarkSubmitFailed("Provider rejected the resend.");
            _logger.LogWarning("Resend of notification {NotificationId} failed to submit: {Error}", notificationId, Describe(ex));
        }

        await _notifications.UpdateAsync(resend, cancellationToken);
        return ResendResult.Sent(resend.Id);
    }

    // ---------------------------------------------------------------- Operator: dispose of content

    public async Task<bool> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
            return false;

        // Remove the text at the provider too — not merely hidden by this application — while the fact a
        // message was sent, and what became of it, survives.
        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            await _provider.RedactBodyAsync(notification.ProviderMessageSid, cancellationToken);
        }

        notification.RedactContent();
        await _notifications.UpdateAsync(notification, cancellationToken);
        return true;
    }

    // ---------------------------------------------------------------- Operator: reconciliation

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Ask the provider for its own record of messages sent from our configured number over the range.
        var providerMessages = await _provider.ListSentMessagesAsync(from, to, cancellationToken);

        // What eShop believes it sent over the same range.
        var local = await _notifications.ListAsync(new SubmittedNotificationsInRangeSpecification(from, to), cancellationToken);
        var localBySid = local
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var providerSids = new HashSet<string>(providerMessages.Select(m => m.Sid));

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();
        foreach (var m in providerMessages)
        {
            if (localBySid.TryGetValue(m.Sid, out var known))
                matched.Add(new ReconciliationEntry(m.Sid, m.Status, known.Id, m.DateSent));
            else
                providerOnly.Add(new ReconciliationEntry(m.Sid, m.Status, null, m.DateSent));
        }

        var eShopOnly = local
            .Where(n => !providerSids.Contains(n.ProviderMessageSid!))
            .Select(n => new ReconciliationEntry(n.ProviderMessageSid!, n.Status, n.Id, n.SentAt))
            .ToList();

        return new ReconciliationReport(
            from, to, _provider.SendingNumber,
            providerMessages.Count, local.Count, matched.Count,
            matched, providerOnly, eShopOnly);
    }

    // ---------------------------------------------------------------- helpers

    private async Task NotifyOwnerAsync(Order order, NotificationType type, string body, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByOwnerSpecification(order.BuyerId), cancellationToken);
        // A shopper with no number on file is simply not messaged.
        foreach (var number in numbers)
        {
            await SendOneAsync(order, type, body, number.PhoneNumber, cancellationToken);
        }
    }

    private async Task SendOneAsync(Order order, NotificationType type, string body, string toE164, CancellationToken cancellationToken)
    {
        var notification = new OrderNotification(order.Id, order.BuyerId, type, toE164, body);
        try
        {
            var msg = await _provider.SendAsync(toE164, body, cancellationToken);
            notification.MarkSubmitted(msg.Sid, msg.Status, msg.ErrorCode, msg.ErrorMessage, msg.DateSent);
        }
        catch (Exception ex)
        {
            notification.MarkSubmitFailed("Provider rejected the message.");
            _logger.LogWarning("Failed to submit {Type} notification for order {OrderId}: {Error}", type, order.Id, Describe(ex));
        }
        await _notifications.AddAsync(notification, cancellationToken);
    }

    private async Task ScheduleFollowUpAsync(Order order, string toE164, CancellationToken cancellationToken)
    {
        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        var notification = new OrderNotification(order.Id, order.BuyerId, NotificationType.DeliveryFollowUp, toE164, Bodies.FollowUp(order.Id));
        try
        {
            var msg = await _provider.ScheduleAsync(toE164, notification.Body!, sendAt, cancellationToken);
            notification.MarkScheduled(msg.Sid, sendAt);
        }
        catch (Exception ex)
        {
            notification.MarkSubmitFailed("Provider rejected the scheduled follow-up.");
            _logger.LogWarning("Failed to schedule delivery follow-up for order {OrderId}: {Error}", order.Id, Describe(ex));
        }
        await _notifications.AddAsync(notification, cancellationToken);
    }

    /// <summary>Bring stored outcomes up to date from the provider, since it cannot call back into us.</summary>
    private async Task RefreshOutcomesAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var n in notifications)
        {
            if (string.IsNullOrEmpty(n.ProviderMessageSid) || NotificationStatus.IsTerminal(n.Status))
                continue;
            try
            {
                var msg = await _provider.GetMessageAsync(n.ProviderMessageSid, cancellationToken);
                n.UpdateOutcome(msg.Status, msg.ErrorCode, msg.ErrorMessage);
                await _notifications.UpdateAsync(n, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to refresh outcome for notification {NotificationId}: {Error}", n.Id, Describe(ex));
            }
        }
    }

    private static NotificationView ToView(OrderNotification n) => new(
        n.Id, n.OrderId, n.Type.ToString(), n.Status, n.ProviderMessageSid,
        n.ProviderErrorCode, n.ProviderErrorMessage, n.ContentRedacted, n.CreatedAt, n.SentAt, n.ScheduledFor);

    /// <summary>A log-safe description of a failure: provider exceptions are already sanitised; others reduced to their type.</summary>
    private static string Describe(Exception ex) =>
        ex is MessagingProviderException ? ex.Message : ex.GetType().Name;

    private static class Bodies
    {
        public static string Placed(int orderId) => $"eShop: thanks! We've received your order #{orderId}.";
        public static string Dispatched(int orderId) => $"eShop: good news — your order #{orderId} is on its way!";
        public static string FollowUp(int orderId) => $"eShop: how did the delivery of order #{orderId} go? Reply and let us know — we value your feedback.";
        public static string Canceled(int orderId) => $"eShop: your order #{orderId} has been cancelled. If this is unexpected, please contact support.";
    }
}
