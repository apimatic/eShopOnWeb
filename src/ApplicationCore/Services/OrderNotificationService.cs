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
    // The provider holds the delivery follow-up; we ask for it a few days after dispatch.
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    // A ship-to address is required by the Order aggregate. Orders placed through this API carry an
    // optional address; when the caller supplies none we fall back to this placeholder so the additive
    // notification flow does not force address collection it does not need.
    private static Address DefaultShipToAddress() => new("N/A", "N/A", "N/A", "N/A", "00000");

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IRepository<Notification> _notificationRepository;
    private readonly IUriComposer _uriComposer;
    private readonly ISmsGateway _smsGateway;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<ContactNumber> contactNumberRepository,
        IRepository<Notification> notificationRepository,
        IUriComposer uriComposer,
        ISmsGateway smsGateway,
        IAppLogger<OrderNotificationService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _contactNumberRepository = contactNumberRepository;
        _notificationRepository = notificationRepository;
        _uriComposer = uriComposer;
        _smsGateway = smsGateway;
        _logger = logger;
    }

    // -------------------- Flow 1: contact numbers --------------------

    public async Task<ContactNumber> RegisterContactNumberAsync(string ownerId, string rawNumber, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(rawNumber, nameof(rawNumber));

        // Reject an unusable destination here, at registration, rather than when a later message fails.
        var lookup = await _smsGateway.LookupNumberAsync(rawNumber, cancellationToken);
        if (!lookup.IsValid || string.IsNullOrEmpty(lookup.CanonicalE164))
        {
            throw new InvalidContactNumberException(lookup.Reason ?? "The number is not a usable SMS destination.");
        }

        // Store the provider's canonical form, not whatever the caller typed.
        var contactNumber = new ContactNumber(ownerId, lookup.CanonicalE164);
        await _contactNumberRepository.AddAsync(contactNumber, cancellationToken);
        _logger.LogInformation("Registered a contact number (id {0}) for a shopper.", contactNumber.Id);
        return contactNumber;
    }

    public async Task<IReadOnlyList<ContactNumber>> GetContactNumbersAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        var numbers = await _contactNumberRepository.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), cancellationToken);
        return numbers;
    }

    public async Task<bool> DeleteContactNumberAsync(string ownerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        var contactNumber = await _contactNumberRepository.GetByIdAsync(contactNumberId, cancellationToken);

        // A number belongs to the shopper who registered it: another shopper must not see or delete it.
        // Report "not found" rather than "forbidden" so ownership is not disclosed.
        if (contactNumber is null || contactNumber.OwnerId != ownerId) return false;

        await _contactNumberRepository.DeleteAsync(contactNumber, cancellationToken);
        return true;
    }

    // -------------------- Flow 2: order lifecycle --------------------

    public async Task<Order> PlaceOrderAsync(string ownerId, IReadOnlyList<OrderLine> lines, Address? shipToAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        if (lines is null || lines.Count == 0)
            throw new ArgumentException("An order must contain at least one line.", nameof(lines));

        foreach (var line in lines)
        {
            Guard.Against.OutOfRange(line.CatalogItemId, nameof(line.CatalogItemId), 1, int.MaxValue);
            Guard.Against.NegativeOrZero(line.Quantity, nameof(line.Quantity));
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        var missing = ids.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
            throw new ArgumentException($"Unknown catalog item id(s): {string.Join(", ", missing)}.", nameof(lines));

        // Reuse the app's existing order/order-item model.
        var orderItems = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(ownerId, shipToAddress ?? DefaultShipToAddress(), orderItems);
        order = await _orderRepository.AddAsync(order, cancellationToken);

        await NotifyAsync(order.Id, ownerId, NotificationKind.OrderPlaced,
            OrderNotificationMessages.OrderPlaced(order.Id), cancellationToken);

        return order;
    }

    public async Task<Order?> DispatchOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null) return null;

        // Tell the shopper it is on its way.
        await NotifyAsync(order.Id, order.BuyerId, NotificationKind.OrderDispatched,
            OrderNotificationMessages.OrderDispatched(order.Id), cancellationToken);

        // Queue the "how did the delivery go?" follow-up with the provider for a few days later.
        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        await ScheduleFollowUpAsync(order.Id, order.BuyerId, sendAt, cancellationToken);

        return order;
    }

    public async Task<Order?> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null) return null;

        // A follow-up that has not yet gone out must never reach the shopper for a cancelled order.
        var existing = await _notificationRepository.ListAsync(new NotificationsByOrderSpecification(order.Id), cancellationToken);
        foreach (var followUp in existing.Where(n => n.IsPendingFollowUp))
        {
            try
            {
                var result = await _smsGateway.CancelScheduledAsync(followUp.ProviderMessageSid!, cancellationToken);
                followUp.UpdateDeliveryStatus(result.Status);
                await _notificationRepository.UpdateAsync(followUp, cancellationToken);
                _logger.LogInformation("Cancelled scheduled follow-up {0} for order {1}.", followUp.ProviderMessageSid!, order.Id);
            }
            catch (Exception ex)
            {
                // Cancelling the follow-up must not fail the cancel operation, but it must be visible.
                _logger.LogWarning("Failed to cancel scheduled follow-up for order {0}: {1}", order.Id, ex.Message);
            }
        }

        // Tell the shopper the order was cancelled.
        await NotifyAsync(order.Id, order.BuyerId, NotificationKind.OrderCancelled,
            OrderNotificationMessages.OrderCancelled(order.Id), cancellationToken);

        return order;
    }

    public async Task<IReadOnlyList<OrderNotificationsView>> GetMyOrdersAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(ownerId), cancellationToken);
        var notifications = await _notificationRepository.ListAsync(new NotificationsByOwnerSpecification(ownerId), cancellationToken);

        await RefreshStatusesAsync(notifications, cancellationToken);

        var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => (IReadOnlyList<Notification>)g.OrderBy(n => n.CreatedDate).ToList());
        return orders
            .Select(o => new OrderNotificationsView(o, byOrder.TryGetValue(o.Id, out var list) ? list : Array.Empty<Notification>()))
            .ToList();
    }

    public async Task<IReadOnlyList<Notification>?> GetOrderNotificationsAsync(int orderId, string ownerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);

        // Shopper-scoped: only the order's owner may see its notifications.
        if (order is null || order.BuyerId != ownerId) return null;

        var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderSpecification(orderId), cancellationToken);
        await RefreshStatusesAsync(notifications, cancellationToken);
        return notifications;
    }

    // -------------------- Flow 3: operator actions --------------------

    public async Task<ResendOutcome?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        // Idempotency: a repeat under the same key returns the message the first attempt produced.
        var priorForKey = await _notificationRepository.FirstOrDefaultAsync(new NotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (priorForKey is not null)
        {
            return new ResendOutcome(priorForKey, WasReplayed: true);
        }

        var original = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (original is null) return null;

        // Bring the original's status up to date; a message already delivered is not re-sent.
        await RefreshStatusesAsync(new[] { original }, cancellationToken);
        if (MessageDeliveryStatus.ReachedHandset(original.ProviderStatus))
        {
            throw new NotificationResendException($"Notification {notificationId} was already delivered; it will not be re-sent.");
        }
        if (original.ContentRedacted || string.IsNullOrEmpty(original.Body))
        {
            throw new NotificationResendException($"Notification {notificationId} has no retained content to re-send.");
        }

        // Persist the resend (with its idempotency key) before contacting the provider, so a retry under
        // the same key can never produce a second message even if the send itself fails midway.
        var resend = Notification.ForImmediate(original.OrderId, original.OwnerId, NotificationKind.Resend, original.ToNumber, original.Body!);
        resend.SetIdempotencyKey(idempotencyKey);
        resend = await _notificationRepository.AddAsync(resend, cancellationToken);

        await TrySendAsync(resend, cancellationToken);
        await _notificationRepository.UpdateAsync(resend, cancellationToken);

        return new ResendOutcome(resend, WasReplayed: false);
    }

    public async Task<Notification?> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null) return null;

        if (notification.ProviderMessageSid is not null)
        {
            // Remove the text at the provider so it is no longer retrievable there either.
            await _smsGateway.RedactBodyAsync(notification.ProviderMessageSid, cancellationToken);
        }

        notification.MarkContentDisposed();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
        _logger.LogInformation("Disposed of the content of notification {0}.", notification.Id);
        return notification;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from) (from, to) = (to, from);

        // Ask the provider for its record of messages from our configured sender across the range.
        var providerMessages = await _smsGateway.ListSentMessagesAsync(from, to, cancellationToken);

        // Narrow to the exact window (the provider's date filter is day-granular).
        var providerInRange = providerMessages
            .Where(m =>
            {
                var stamp = m.DateSent ?? m.DateCreated;
                return stamp == null || (stamp >= from && stamp <= to);
            })
            .ToList();

        var eShopNotifications = await _notificationRepository.ListAsync(new NotificationsCreatedBetweenSpecification(from, to), cancellationToken);
        var eShopBySid = eShopNotifications
            .Where(n => n.ProviderMessageSid is not null)
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();
        var seenSids = new HashSet<string>();

        foreach (var pm in providerInRange)
        {
            seenSids.Add(pm.Sid);
            if (eShopBySid.TryGetValue(pm.Sid, out var n))
            {
                matched.Add(new ReconciliationEntry(pm.Sid, pm.Status, n.ProviderStatus, n.OrderId, n.Kind, pm.DateSent ?? pm.DateCreated));
            }
            else
            {
                // The provider knows about it and eShop does not.
                providerOnly.Add(new ReconciliationEntry(pm.Sid, pm.Status, null, null, null, pm.DateSent ?? pm.DateCreated));
            }
        }

        // eShop believes it sent these, but the provider's record for the range does not show them.
        var eShopOnly = eShopNotifications
            .Where(n => n.ProviderMessageSid is null || !seenSids.Contains(n.ProviderMessageSid))
            .Select(n => new ReconciliationEntry(n.ProviderMessageSid, null, n.ProviderStatus, n.OrderId, n.Kind, n.CreatedDate))
            .ToList();

        return new ReconciliationReport(from, to, _smsGateway.ConfiguredFromNumber, matched, providerOnly, eShopOnly);
    }

    // -------------------- helpers --------------------

    /// <summary>
    /// Sends one message to every number the shopper has on file, recording a notification for each.
    /// A shopper with no number on file is simply not messaged. Sending is best-effort.
    /// </summary>
    private async Task NotifyAsync(int orderId, string ownerId, NotificationKind kind, string body, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumberRepository.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), cancellationToken);
        foreach (var number in numbers)
        {
            var notification = Notification.ForImmediate(orderId, ownerId, kind, number.PhoneNumber, body);
            notification = await _notificationRepository.AddAsync(notification, cancellationToken);
            await TrySendAsync(notification, cancellationToken);
            await _notificationRepository.UpdateAsync(notification, cancellationToken);
        }
    }

    private async Task ScheduleFollowUpAsync(int orderId, string ownerId, DateTimeOffset sendAt, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumberRepository.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), cancellationToken);
        foreach (var number in numbers)
        {
            var body = OrderNotificationMessages.DeliveryFollowUp(orderId);
            var notification = Notification.ForScheduled(orderId, ownerId, NotificationKind.DeliveryFollowUp, number.PhoneNumber, body, sendAt);
            notification = await _notificationRepository.AddAsync(notification, cancellationToken);
            try
            {
                var result = await _smsGateway.ScheduleAsync(number.PhoneNumber, body, sendAt, cancellationToken);
                if (result.Accepted && result.ProviderMessageSid is not null)
                    notification.RecordProviderAccepted(result.ProviderMessageSid, result.Status, result.ErrorCode);
                else
                    notification.RecordSendFailed(result.ErrorCode);
            }
            catch (Exception ex)
            {
                notification.RecordSendFailed();
                _logger.LogWarning("Failed to schedule delivery follow-up for order {0}: {1}", orderId, ex.Message);
            }
            await _notificationRepository.UpdateAsync(notification, cancellationToken);
        }
    }

    /// <summary>Attempts an immediate send, folding the outcome into the notification. Never throws.</summary>
    private async Task TrySendAsync(Notification notification, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _smsGateway.SendAsync(notification.ToNumber, notification.Body!, cancellationToken);
            if (result.Accepted && result.ProviderMessageSid is not null)
                notification.RecordProviderAccepted(result.ProviderMessageSid, result.Status, result.ErrorCode);
            else
                notification.RecordSendFailed(result.ErrorCode);
        }
        catch (Exception ex)
        {
            notification.RecordSendFailed();
            _logger.LogWarning("Failed to send a {0} notification for order {1}: {2}", notification.Kind, notification.OrderId, ex.Message);
        }
    }

    /// <summary>
    /// Refreshes non-terminal notifications from the provider so reads report the current outcome. There is
    /// no callback URL for this app, so state has to be pulled from the provider on demand. Best-effort.
    /// </summary>
    private async Task RefreshStatusesAsync(IEnumerable<Notification> notifications, CancellationToken cancellationToken)
    {
        foreach (var n in notifications)
        {
            if (n.ProviderMessageSid is null) continue;
            if (MessageDeliveryStatus.IsTerminal(n.ProviderStatus)) continue;
            try
            {
                var state = await _smsGateway.FetchAsync(n.ProviderMessageSid, cancellationToken);
                if (state is not null)
                {
                    n.UpdateDeliveryStatus(state.Status, state.ErrorCode);
                    await _notificationRepository.UpdateAsync(n, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to refresh status for notification {0}: {1}", n.Id, ex.Message);
            }
        }
    }
}
