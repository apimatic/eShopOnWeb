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
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Coordinates order-progress SMS notifications. A message that cannot be sent is recorded and the
/// underlying operation still succeeds; a shopper with no number on file is simply not messaged.
/// Shopper phone numbers are never written to logs.
/// </summary>
public class NotificationService : INotificationService
{
    /// <summary>How far ahead the "how did the delivery go?" follow-up is queued with the provider.</summary>
    public static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<Notification> _notifications;
    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IUriComposer _uriComposer;
    private readonly ISmsProvider _sms;
    private readonly IAppLogger<NotificationService> _logger;

    public NotificationService(
        IRepository<ContactNumber> contactNumbers,
        IRepository<Notification> notifications,
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IUriComposer uriComposer,
        ISmsProvider sms,
        IAppLogger<NotificationService> logger)
    {
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _orders = orders;
        _catalogItems = catalogItems;
        _uriComposer = uriComposer;
        _sms = sms;
        _logger = logger;
    }

    // ============================ Flow 1: contact numbers ==================================

    public async Task<ContactNumber> RegisterContactNumberAsync(string ownerId, string rawNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawNumber))
            throw new ContactNumberValidationException("A phone number is required.");

        PhoneLookupResult lookup;
        try
        {
            lookup = await _sms.LookupAsync(rawNumber, cancellationToken);
        }
        catch (Exception ex)
        {
            // A lookup failure is not the same as an invalid number; surface it as a validation problem
            // without ever echoing the submitted number.
            throw new ContactNumberValidationException($"The phone number could not be validated with the provider: {ex.Message}");
        }

        if (!lookup.IsValid || string.IsNullOrWhiteSpace(lookup.CanonicalNumber))
        {
            var reasons = lookup.ValidationErrors.Count > 0 ? string.Join(", ", lookup.ValidationErrors) : "not a usable destination";
            throw new ContactNumberValidationException($"The phone number is not a usable destination ({reasons}).");
        }

        var canonical = lookup.CanonicalNumber!;

        // Store the provider's canonical form. If the shopper already has this number on file, keep the
        // existing registration rather than creating a duplicate that would double-message them.
        var existing = await _contactNumbers.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), cancellationToken);
        var already = existing.FirstOrDefault(c => c.PhoneNumber == canonical);
        if (already is not null)
            return already;

        var contactNumber = new ContactNumber(ownerId, canonical);
        return await _contactNumbers.AddAsync(contactNumber, cancellationToken);
    }

    public async Task<IReadOnlyList<ContactNumber>> GetContactNumbersAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        return await _contactNumbers.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), cancellationToken);
    }

    public async Task DeleteContactNumberAsync(string ownerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        var contactNumber = await _contactNumbers.GetByIdAsync(contactNumberId, cancellationToken);
        if (contactNumber is null || contactNumber.OwnerId != ownerId)
            throw new ResourceNotFoundException($"Contact number {contactNumberId} was not found.");

        // Nothing may be sent to this number again: call off any follow-up already queued with the
        // provider that would otherwise still reach it.
        var pending = await _notifications.ListAsync(
            new PendingScheduledNotificationsByRecipientSpecification(ownerId, contactNumber.PhoneNumber), cancellationToken);
        foreach (var n in pending)
            await CancelScheduledSafelyAsync(n, cancellationToken);

        await _contactNumbers.DeleteAsync(contactNumber, cancellationToken);
    }

    // ============================ Flow 2: orders ===========================================

    public async Task<Order> PlaceOrderAsync(string ownerId, IReadOnlyList<OrderLineRequest> lines, Address shipToAddress, CancellationToken cancellationToken = default)
    {
        if (lines is null || lines.Count == 0)
            throw new ArgumentException("An order must contain at least one item.", nameof(lines));
        if (lines.Any(l => l.Quantity <= 0))
            throw new ArgumentException("Each order line must have a quantity of at least one.", nameof(lines));

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);

        var items = new List<OrderItem>();
        foreach (var line in lines)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId);
            if (catalogItem is null)
                throw new ResourceNotFoundException($"Catalog item {line.CatalogItemId} was not found.");

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            items.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(ownerId, shipToAddress, items);
        order = await _orders.AddAsync(order, cancellationToken);

        // Tell the shopper their order was placed.
        await NotifyOwnerNumbersAsync(order, NotificationType.OrderPlaced, cancellationToken);

        return order;
    }

    public async Task DispatchOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
            throw new ResourceNotFoundException($"Order {orderId} was not found.");

        order.MarkDispatched();
        await _orders.UpdateAsync(order, cancellationToken);

        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByOwnerSpecification(order.BuyerId), cancellationToken);
        foreach (var cn in numbers)
        {
            // The order is on its way...
            await SendImmediateAsync(order, cn.PhoneNumber, NotificationType.OrderDispatched, cancellationToken);
            // ...and a follow-up asking how the delivery went is queued with the provider for a few days later.
            await ScheduleFollowUpAsync(order, cn.PhoneNumber, cancellationToken);
        }
    }

    public async Task CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
            throw new ResourceNotFoundException($"Order {orderId} was not found.");

        order.Cancel();
        await _orders.UpdateAsync(order, cancellationToken);

        // Critical: a follow-up that has not yet gone out must never reach the shopper.
        var pending = await _notifications.ListAsync(new PendingScheduledNotificationsByOrderSpecification(orderId), cancellationToken);
        foreach (var n in pending)
            await CancelScheduledSafelyAsync(n, cancellationToken);

        // Tell the shopper the order was cancelled.
        await NotifyOwnerNumbersAsync(order, NotificationType.OrderCancelled, cancellationToken);
    }

    public async Task<IReadOnlyList<OrderWithNotifications>> GetMyOrdersAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(ownerId), cancellationToken);
        var notifications = await _notifications.ListAsync(new NotificationsByOwnerSpecification(ownerId), cancellationToken);

        await RefreshProviderStateAsync(notifications, cancellationToken);

        var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => (IReadOnlyList<Notification>)g.OrderBy(n => n.Id).ToList());

        return orders
            .OrderByDescending(o => o.Id)
            .Select(o => new OrderWithNotifications(o, byOrder.TryGetValue(o.Id, out var ns) ? ns : Array.Empty<Notification>()))
            .ToList();
    }

    public async Task<IReadOnlyList<Notification>> GetOrderNotificationsAsync(string callerId, int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order is null || order.BuyerId != callerId)
            throw new ResourceNotFoundException($"Order {orderId} was not found.");

        var notifications = await _notifications.ListAsync(new NotificationsByOrderSpecification(orderId), cancellationToken);
        await RefreshProviderStateAsync(notifications, cancellationToken);
        return notifications;
    }

    // ============================ Flow 3: operator actions =================================

    public async Task<Notification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new ArgumentException("An idempotency key is required.", nameof(idempotencyKey));

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (original is null)
            throw new ResourceNotFoundException($"Notification {notificationId} was not found.");

        // Repeating a request under the same key must not send a second message.
        var priorResends = await _notifications.ListAsync(
            new ResendByIdempotencyKeySpecification(notificationId, idempotencyKey), cancellationToken);
        var duplicate = priorResends.FirstOrDefault();
        if (duplicate is not null)
            return duplicate;

        // Reconstruct the message. If the original's content was disposed of, rebuild it from its type.
        var body = original.Body ?? BuildBody(original.Type, original.OrderId);

        var resend = new Notification(
            original.OwnerId, original.OrderId, original.Recipient, original.Type, body,
            idempotencyKey: idempotencyKey, resendOfNotificationId: notificationId);

        // Persist first so the idempotency key is recorded even if the send itself fails.
        resend = await _notifications.AddAsync(resend, cancellationToken);
        await SendAndRecordAsync(resend, cancellationToken);

        return resend;
    }

    public async Task DisposeNotificationContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
            throw new ResourceNotFoundException($"Notification {notificationId} was not found.");

        // The text must no longer be retrievable from the provider either — redact it there first.
        // Only report success once the provider has disposed of the content.
        if (!string.IsNullOrWhiteSpace(notification.ProviderMessageSid))
            await _sms.RedactBodyAsync(notification.ProviderMessageSid!, cancellationToken);

        notification.DisposeContent();
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Ask the provider for its record of messages sent from our configured number over the range.
        var providerMessages = await _sms.ListMessagesFromConfiguredSenderAsync(from, to, cancellationToken);

        // Narrow to the exact window (the provider filters by date; tighten to the date-time bounds here).
        var provider = providerMessages
            .Where(m => !string.IsNullOrWhiteSpace(m.Sid))
            .Where(m =>
            {
                var when = m.DateSent ?? m.DateCreated;
                return when is null || (when >= from && when <= to);
            })
            .GroupBy(m => m.Sid!)
            .ToDictionary(g => g.Key, g => g.First());

        // What eShop believes it sent in this window: notifications that were handed to the provider
        // (they carry a provider message id) and created within the range.
        var allNotifications = await _notifications.ListAsync(cancellationToken);
        var eShop = allNotifications
            .Where(n => !string.IsNullOrWhiteSpace(n.ProviderMessageSid))
            .Where(n => n.CreatedDate >= from && n.CreatedDate <= to)
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();
        var eShopOnly = new List<ReconciliationEntry>();

        foreach (var kvp in provider)
        {
            if (eShop.TryGetValue(kvp.Key, out var n))
            {
                matched.Add(new ReconciliationEntry
                {
                    ProviderMessageSid = kvp.Key,
                    ProviderStatus = kvp.Value.Status,
                    ProviderDateSent = kvp.Value.DateSent ?? kvp.Value.DateCreated,
                    NotificationId = n.Id,
                    OrderId = n.OrderId,
                    EShopStatus = n.ProviderStatus
                });
            }
            else
            {
                providerOnly.Add(new ReconciliationEntry
                {
                    ProviderMessageSid = kvp.Key,
                    ProviderStatus = kvp.Value.Status,
                    ProviderDateSent = kvp.Value.DateSent ?? kvp.Value.DateCreated
                });
            }
        }

        foreach (var kvp in eShop)
        {
            if (!provider.ContainsKey(kvp.Key))
            {
                eShopOnly.Add(new ReconciliationEntry
                {
                    ProviderMessageSid = kvp.Key,
                    NotificationId = kvp.Value.Id,
                    OrderId = kvp.Value.OrderId,
                    EShopStatus = kvp.Value.ProviderStatus
                });
            }
        }

        return new ReconciliationReport
        {
            From = from,
            To = to,
            Matched = matched,
            ProviderOnly = providerOnly,
            EShopOnly = eShopOnly
        };
    }

    // ============================ helpers ==================================================

    private async Task NotifyOwnerNumbersAsync(Order order, NotificationType type, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByOwnerSpecification(order.BuyerId), cancellationToken);
        // A shopper with no number on file is simply not messaged.
        foreach (var cn in numbers)
            await SendImmediateAsync(order, cn.PhoneNumber, type, cancellationToken);
    }

    private async Task SendImmediateAsync(Order order, string recipient, NotificationType type, CancellationToken cancellationToken)
    {
        var body = BuildBody(type, order.Id);
        var notification = new Notification(order.BuyerId, order.Id, recipient, type, body);
        notification = await _notifications.AddAsync(notification, cancellationToken);
        await SendAndRecordAsync(notification, cancellationToken);
    }

    private async Task SendAndRecordAsync(Notification notification, CancellationToken cancellationToken)
    {
        try
        {
            var msg = await _sms.SendAsync(notification.Recipient, notification.Body!, cancellationToken);
            notification.RecordSent(msg.Sid, msg.Status, msg.ErrorCode, msg.ErrorMessage);
        }
        catch (Exception ex)
        {
            // A message that cannot be sent must never fail the underlying operation.
            notification.MarkSendFailed(ex.Message);
            _logger.LogWarning("Notification {0} for order {1} could not be sent: {2}", notification.Id, notification.OrderId, ex.Message);
        }
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    private async Task ScheduleFollowUpAsync(Order order, string recipient, CancellationToken cancellationToken)
    {
        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        var body = BuildBody(NotificationType.DeliveryFollowUp, order.Id);
        var notification = new Notification(order.BuyerId, order.Id, recipient, NotificationType.DeliveryFollowUp, body,
            isScheduled: true, scheduledFor: sendAt);
        notification = await _notifications.AddAsync(notification, cancellationToken);

        try
        {
            var msg = await _sms.ScheduleAsync(recipient, body, sendAt, cancellationToken);
            notification.RecordSent(msg.Sid, msg.Status, msg.ErrorCode, msg.ErrorMessage);
        }
        catch (Exception ex)
        {
            notification.MarkSendFailed(ex.Message);
            _logger.LogWarning("Follow-up {0} for order {1} could not be scheduled: {2}", notification.Id, notification.OrderId, ex.Message);
        }
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    private async Task CancelScheduledSafelyAsync(Notification notification, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(notification.ProviderMessageSid))
        {
            notification.MarkCanceled();
            await _notifications.UpdateAsync(notification, cancellationToken);
            return;
        }

        try
        {
            var msg = await _sms.CancelScheduledAsync(notification.ProviderMessageSid!, cancellationToken);
            notification.RefreshProviderState(msg.Status ?? "canceled", msg.ErrorCode, msg.ErrorMessage);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Scheduled notification {0} for order {1} could not be cancelled at the provider: {2}",
                notification.Id, notification.OrderId, ex.Message);
        }
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    private async Task RefreshProviderStateAsync(IEnumerable<Notification> notifications, CancellationToken cancellationToken)
    {
        foreach (var n in notifications)
        {
            if (string.IsNullOrWhiteSpace(n.ProviderMessageSid)) continue;
            if (!n.IsInNonTerminalState()) continue;

            try
            {
                var msg = await _sms.FetchAsync(n.ProviderMessageSid!, cancellationToken);
                n.RefreshProviderState(msg.Status, msg.ErrorCode, msg.ErrorMessage);
                await _notifications.UpdateAsync(n, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not refresh notification {0}: {1}", n.Id, ex.Message);
            }
        }
    }

    private static string BuildBody(NotificationType type, int orderId) => type switch
    {
        NotificationType.OrderPlaced => $"eShopOnWeb: Thanks! Your order #{orderId} has been placed.",
        NotificationType.OrderDispatched => $"eShopOnWeb: Good news - your order #{orderId} is on its way!",
        NotificationType.DeliveryFollowUp => $"eShopOnWeb: How did the delivery of your order #{orderId} go? Reply to let us know.",
        NotificationType.OrderCancelled => $"eShopOnWeb: Your order #{orderId} has been cancelled. Contact us if this is unexpected.",
        _ => $"eShopOnWeb: Update on your order #{orderId}."
    };
}
