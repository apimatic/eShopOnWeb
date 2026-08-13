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
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Twilio;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.Services.Twilio;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Places orders and drives the SMS notifications that go out as an order moves. A failure to send
/// never fails the underlying operation; a shopper with no number on file is simply not messaged.
/// The record kept for each message carries the provider's identifier and current delivery outcome
/// so later requests can both act on and report on it.
/// </summary>
public class OrderNotificationService : IOrderNotificationService
{
    // The API places orders from catalog items and quantities only; the existing order model still
    // requires a shipping address, so a clearly-marked placeholder is used when none is supplied.
    private static readonly Address UnspecifiedAddress =
        new("Not specified", "Not specified", "NA", "Not specified", "00000");

    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<Notification> _notifications;
    private readonly ITwilioMessagingClient _messaging;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IRepository<ContactNumber> contactNumbers,
        IRepository<Notification> notifications,
        ITwilioMessagingClient messaging,
        IOptions<TwilioSettings> settings,
        IAppLogger<OrderNotificationService> logger)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _messaging = messaging;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, CancellationToken cancellationToken = default)
    {
        if (lines == null || lines.Count == 0)
        {
            throw new InvalidOrderRequestException("An order must contain at least one item.");
        }

        if (lines.Any(l => l.Quantity <= 0))
        {
            throw new InvalidOrderRequestException("Every order line must have a quantity of at least one.");
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        var byId = catalogItems.ToDictionary(c => c.Id);

        var missing = ids.Where(id => !byId.ContainsKey(id)).ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOrderRequestException($"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var items = lines.Select(line =>
        {
            var catalogItem = byId[line.CatalogItemId];
            var pictureUri = string.IsNullOrEmpty(catalogItem.PictureUri) ? "eCatalog-item-default.png" : catalogItem.PictureUri;
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, UnspecifiedAddress, items);
        order = await _orders.AddAsync(order, cancellationToken);

        _logger.LogInformation("Placed order {OrderId} for buyer {BuyerId} with {LineCount} line(s).", order.Id, buyerId, items.Count);

        // Tell the shopper the order was placed. This must not be able to fail the placement.
        await NotifyAsync(order, NotificationKind.OrderPlaced, Messages.Placed(order.Id), schedule: false, cancellationToken);

        return order;
    }

    public async Task<bool> DispatchOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order == null)
        {
            return false;
        }

        order.Dispatch(); // throws OrderStatusException on an invalid transition — before anything is sent
        await _orders.UpdateAsync(order, cancellationToken);

        _logger.LogInformation("Dispatched order {OrderId}.", order.Id);

        // Tell the shopper it is on its way...
        await NotifyAsync(order, NotificationKind.OrderDispatched, Messages.Dispatched(order.Id), schedule: false, cancellationToken);
        // ...and queue the "how did delivery go?" follow-up with the provider for a few days later.
        await NotifyAsync(order, NotificationKind.DeliveryFollowUp, Messages.FollowUp(order.Id), schedule: true, cancellationToken);

        return true;
    }

    public async Task<bool> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order == null)
        {
            return false;
        }

        order.Cancel(); // throws OrderStatusException if already cancelled — before anything is sent
        await _orders.UpdateAsync(order, cancellationToken);

        _logger.LogInformation("Cancelled order {OrderId}.", order.Id);

        // Call off any follow-up that has not gone out yet, so a "how did delivery go?" message for a
        // cancelled order can never reach the customer.
        await CancelPendingFollowUpsAsync(order.Id, cancellationToken);

        // Tell the shopper it was cancelled.
        await NotifyAsync(order, NotificationKind.OrderCancelled, Messages.Cancelled(order.Id), schedule: false, cancellationToken);

        return true;
    }

    public async Task<IReadOnlyList<OrderWithNotifications>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orders.ListAsync(new CustomerOrdersSpecification(buyerId), cancellationToken);

        var result = new List<OrderWithNotifications>();
        foreach (var order in orders.OrderByDescending(o => o.OrderDate))
        {
            var notifications = await _notifications.ListAsync(new NotificationsByOrderSpecification(order.Id), cancellationToken);
            await RefreshAsync(notifications, cancellationToken);
            result.Add(new OrderWithNotifications(order, notifications));
        }

        return result;
    }

    public async Task<IReadOnlyList<Notification>?> GetOrderNotificationsAsync(int orderId, string callerId, bool callerIsAdmin, CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order == null)
        {
            return null;
        }

        // A shopper may only see their own order's notifications; an operator may see any order's.
        if (!callerIsAdmin && !string.Equals(order.BuyerId, callerId, StringComparison.Ordinal))
        {
            return null;
        }

        var notifications = await _notifications.ListAsync(new NotificationsByOrderSpecification(orderId), cancellationToken);
        await RefreshAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<ResendResult?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        // A repeat under the same idempotency key must not send a second message.
        var priorForKey = await _notifications.FirstOrDefaultAsync(
            new NotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (priorForKey != null)
        {
            _logger.LogInformation("Resend for notification {NotificationId} is a duplicate of idempotency key; no message sent.", notificationId);
            return new ResendResult(priorForKey, WasDuplicate: true);
        }

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (original == null)
        {
            return null;
        }

        var body = original.Body ?? Messages.ForKind(original.Kind, original.OrderId);

        var resend = new Notification(
            original.OrderId,
            original.BuyerId,
            original.ToNumber,
            original.Kind,
            body,
            scheduledSendAt: null,
            idempotencyKey: idempotencyKey,
            resendOfNotificationId: original.Id);

        resend = await _notifications.AddAsync(resend, cancellationToken);

        await SendAndRecordAsync(resend, schedule: false, cancellationToken);

        _logger.LogInformation("Resent notification {OriginalId} as {ResendId}.", original.Id, resend.Id);
        return new ResendResult(resend, WasDuplicate: false);
    }

    public async Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (notification == null)
        {
            return false;
        }

        // Redact at the provider first so the text is no longer retrievable there. Only once that
        // succeeds do we clear the local copy and mark it disposed (so a failure can be retried).
        if (!string.IsNullOrEmpty(notification.ProviderSid))
        {
            await _messaging.RedactBodyAsync(notification.ProviderSid!, cancellationToken);
        }

        notification.DisposeContent();
        await _notifications.UpdateAsync(notification, cancellationToken);

        _logger.LogInformation("Disposed of content for notification {NotificationId}.", notification.Id);
        return true;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Ask the provider only for this application's own sending number's messages.
        var providerMessages = await _messaging.ListBySenderAsync(_settings.FromNumber, from, to, cancellationToken);
        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid))
            .GroupBy(m => m.Sid)
            .ToDictionary(g => g.Key, g => g.First());

        var eShopNotifications = await _notifications.ListAsync(new SentNotificationsByDateRangeSpecification(from, to), cancellationToken);
        var eShopBySid = eShopNotifications
            .Where(n => !string.IsNullOrEmpty(n.ProviderSid))
            .GroupBy(n => n.ProviderSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new List<ReconciliationMatch>();
        var providerOnly = new List<ReconciliationProviderOnly>();
        var eShopOnly = new List<ReconciliationEShopOnly>();

        foreach (var (sid, message) in providerBySid)
        {
            if (eShopBySid.TryGetValue(sid, out var notification))
            {
                matched.Add(new ReconciliationMatch(sid, notification.Id, message.Status, notification.Status.ToString(), message.DateSent));
            }
            else
            {
                providerOnly.Add(new ReconciliationProviderOnly(sid, message.Status, message.DateSent));
            }
        }

        foreach (var (sid, notification) in eShopBySid)
        {
            if (!providerBySid.ContainsKey(sid))
            {
                eShopOnly.Add(new ReconciliationEShopOnly(notification.Id, sid, notification.Status.ToString()));
            }
        }

        _logger.LogInformation(
            "Reconciliation over range produced {Matched} matched, {ProviderOnly} provider-only, {EShopOnly} eShop-only.",
            matched.Count, providerOnly.Count, eShopOnly.Count);

        return new ReconciliationReport(
            from, to, _settings.FromNumber,
            providerBySid.Count, eShopBySid.Count,
            matched, providerOnly, eShopOnly);
    }

    /// <summary>Sends one message per contact number the buyer has on file. Never throws.</summary>
    private async Task NotifyAsync(Order order, NotificationKind kind, string body, bool schedule, CancellationToken cancellationToken)
    {
        try
        {
            var contactNumbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(order.BuyerId), cancellationToken);
            if (contactNumbers.Count == 0)
            {
                // A shopper with no number on file is simply not messaged.
                _logger.LogInformation("Order {OrderId}: buyer has no contact number on file; not messaging.", order.Id);
                return;
            }

            DateTimeOffset? scheduledSendAt = schedule ? DateTimeOffset.UtcNow.Add(_settings.FollowUpDelay) : null;

            foreach (var contactNumber in contactNumbers)
            {
                var notification = new Notification(order.Id, order.BuyerId, contactNumber.PhoneNumber, kind, body, scheduledSendAt);
                notification = await _notifications.AddAsync(notification, cancellationToken);
                await SendAndRecordAsync(notification, schedule, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            // The order operation must succeed even if messaging as a whole falls over.
            _logger.LogWarning("Order {OrderId}: notifying kind {Kind} failed but the operation stands: {Error}.", order.Id, kind, ex.GetType().Name);
        }
    }

    /// <summary>Hands one notification to the provider and records the outcome. Never throws.</summary>
    private async Task SendAndRecordAsync(Notification notification, bool schedule, CancellationToken cancellationToken)
    {
        try
        {
            TwilioMessageResource message;
            if (schedule && notification.ScheduledSendAt.HasValue)
            {
                message = await _messaging.ScheduleAsync(notification.ToNumber, notification.Body!, notification.ScheduledSendAt.Value, cancellationToken);
                notification.RecordAccepted(message.Sid, message.Status, sentAt: null);
            }
            else
            {
                message = await _messaging.SendAsync(notification.ToNumber, notification.Body!, cancellationToken);
                notification.RecordAccepted(message.Sid, message.Status, sentAt: DateTimeOffset.UtcNow);
            }
        }
        catch (TwilioApiException ex)
        {
            // Provider rejected the create call outright; record it as a send failure (no message exists).
            notification.RecordSendFailure(ex.ProviderErrorCode, ex.ProviderErrorMessage);
            _logger.LogWarning("Notification {NotificationId}: provider rejected the send (code {Code}).", notification.Id, ex.ProviderErrorCode);
        }
        catch (Exception ex)
        {
            notification.RecordSendFailure(null, null);
            _logger.LogWarning("Notification {NotificationId}: send failed ({Error}).", notification.Id, ex.GetType().Name);
        }

        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    /// <summary>Cancels every not-yet-sent follow-up for an order at the provider.</summary>
    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var pending = await _notifications.ListAsync(new PendingFollowUpsByOrderSpecification(orderId), cancellationToken);
        foreach (var followUp in pending)
        {
            try
            {
                await _messaging.CancelScheduledAsync(followUp.ProviderSid!, cancellationToken);
                followUp.MarkCanceled();
                await _notifications.UpdateAsync(followUp, cancellationToken);
                _logger.LogInformation("Called off scheduled follow-up {NotificationId} for order {OrderId}.", followUp.Id, orderId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Order {OrderId}: could not cancel follow-up {NotificationId} ({Error}).", orderId, followUp.Id, ex.GetType().Name);
            }
        }
    }

    /// <summary>Refreshes non-terminal notifications from the provider so outcomes are current. Never throws.</summary>
    private async Task RefreshAsync(IReadOnlyList<Notification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderSid) || notification.IsTerminal)
            {
                continue;
            }

            try
            {
                var message = await _messaging.FetchAsync(notification.ProviderSid!, cancellationToken);
                notification.ApplyProviderStatus(message.Status, message.ErrorCode, message.ErrorMessage);
                if (message.DateSent.HasValue && notification.SentAt == null && notification.Status != NotificationStatus.Scheduled)
                {
                    // A scheduled follow-up that has since gone out now counts as sent.
                    notification.RecordAccepted(notification.ProviderSid!, message.Status, message.DateSent);
                }
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Notification {NotificationId}: refresh failed ({Error}).", notification.Id, ex.GetType().Name);
            }
        }
    }

    private static class Messages
    {
        public static string Placed(int orderId) =>
            $"eShop: your order #{orderId} has been placed. Thank you for shopping with us!";

        public static string Dispatched(int orderId) =>
            $"eShop: good news - your order #{orderId} is on its way!";

        public static string FollowUp(int orderId) =>
            $"eShop: how did the delivery of your order #{orderId} go? We'd love your feedback.";

        public static string Cancelled(int orderId) =>
            $"eShop: your order #{orderId} has been cancelled. If this is unexpected, please contact support.";

        public static string ForKind(NotificationKind kind, int orderId) => kind switch
        {
            NotificationKind.OrderPlaced => Placed(orderId),
            NotificationKind.OrderDispatched => Dispatched(orderId),
            NotificationKind.DeliveryFollowUp => FollowUp(orderId),
            NotificationKind.OrderCancelled => Cancelled(orderId),
            _ => $"eShop: an update about your order #{orderId}."
        };
    }
}
