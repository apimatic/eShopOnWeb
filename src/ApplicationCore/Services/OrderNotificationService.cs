using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Coordinates the SMS notifications raised as an order moves. Sending is always best-effort: a
/// message that cannot be sent is recorded as such and never fails the order operation.
/// </summary>
public class OrderNotificationService : IOrderNotificationService
{
    // The provider requires a scheduled send to be at least 15 minutes and at most 7 days out.
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    // SMS delivery states from which no further change is expected.
    private static readonly HashSet<string> TerminalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        NotificationStatuses.Delivered,
        NotificationStatuses.Undelivered,
        NotificationStatuses.Failed,
        NotificationStatuses.Canceled,
        NotificationStatuses.SubmissionFailed
    };

    private readonly IRepository<Order> _orders;
    private readonly IReadRepository<CatalogItem> _catalogItems;
    private readonly IReadRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly ITwilioMessagingClient _twilio;
    private readonly IUriComposer _uriComposer;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orders,
        IReadRepository<CatalogItem> catalogItems,
        IReadRepository<ContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        ITwilioMessagingClient twilio,
        IUriComposer uriComposer,
        TwilioSettings settings,
        IAppLogger<OrderNotificationService> logger)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _twilio = twilio;
        _uriComposer = uriComposer;
        _settings = settings;
        _logger = logger;
    }

    public async Task<Result<Order>> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines, CancellationToken cancellationToken = default)
    {
        var cleaned = (lines ?? Array.Empty<OrderLine>())
            .Where(l => l is not null && l.Quantity > 0 && l.CatalogItemId > 0)
            .ToList();
        if (cleaned.Count == 0)
        {
            return Result<Order>.Invalid(new List<ValidationError> { new() { ErrorMessage = "At least one order line with a positive quantity is required." } });
        }

        var ids = cleaned.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        var missing = ids.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
        {
            return Result<Order>.Invalid(new List<ValidationError>
            {
                new() { Identifier = "items", ErrorMessage = $"Unknown catalog item id(s): {string.Join(", ", missing)}." }
            });
        }

        var items = cleaned.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        // Reuse the existing order/order-item model. This API surface does not collect a shipping
        // address, so a placeholder is used; the notification flow does not depend on it.
        var shipToAddress = new Address("N/A", "N/A", "N/A", "N/A", "00000");
        var order = new Order(buyerId, shipToAddress, items);
        await _orders.AddAsync(order, cancellationToken);

        await SendImmediateAsync(order, NotificationKind.OrderPlaced,
            $"Your eShop order #{order.Id} has been placed. Thank you!", cancellationToken);

        return Result<Order>.Success(order);
    }

    public async Task<Result> DispatchOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            return Result.NotFound();
        }
        if (order.Status == OrderStatus.Cancelled)
        {
            return Result.Error("Order was cancelled and cannot be dispatched.");
        }

        order.MarkDispatched();
        await _orders.UpdateAsync(order, cancellationToken);

        await SendImmediateAsync(order, NotificationKind.OrderDispatched,
            $"Good news! Your eShop order #{order.Id} is on its way.", cancellationToken);

        // Queue the "how did delivery go?" follow-up with the provider for a few days later.
        await ScheduleFollowUpAsync(order, cancellationToken);

        return Result.Success();
    }

    public async Task<Result> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            return Result.NotFound();
        }

        order.MarkCancelled();
        await _orders.UpdateAsync(order, cancellationToken);

        // Call off any follow-up that has not yet gone out FIRST — it must never reach the shopper.
        await CancelPendingFollowUpsAsync(orderId, cancellationToken);

        await SendImmediateAsync(order, NotificationKind.OrderCancelled,
            $"Your eShop order #{order.Id} has been cancelled. If this is unexpected, please contact support.", cancellationToken);

        return Result.Success();
    }

    public async Task<IReadOnlyList<OrderWithNotifications>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var notifications = await _notifications.ListAsync(new OrderNotificationsByBuyerSpecification(buyerId), cancellationToken);

        await RefreshOutcomesAsync(notifications, cancellationToken);

        var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => (IReadOnlyList<OrderNotification>)g.ToList());
        return orders
            .Select(o => new OrderWithNotifications(o, byOrder.TryGetValue(o.Id, out var ns) ? ns : Array.Empty<OrderNotification>()))
            .ToList();
    }

    public async Task<Result<IReadOnlyList<OrderNotification>>> GetOrderNotificationsAsync(int orderId, string buyerId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order is null || !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            // Do not reveal the existence of another shopper's order.
            return Result<IReadOnlyList<OrderNotification>>.NotFound();
        }

        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderSpecification(orderId), cancellationToken);
        await RefreshOutcomesAsync(notifications, cancellationToken);
        return Result<IReadOnlyList<OrderNotification>>.Success(notifications);
    }

    public async Task<Result<OrderNotification>> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Result<OrderNotification>.Invalid(new List<ValidationError> { new() { ErrorMessage = "An idempotency key is required." } });
        }

        // Repeating a request under the same key must not send a second message.
        var already = await _notifications.FirstOrDefaultAsync(new OrderNotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (already is not null)
        {
            return Result<OrderNotification>.Success(already);
        }

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (original is null)
        {
            return Result<OrderNotification>.NotFound();
        }
        if (original.ContentDisposed || string.IsNullOrEmpty(original.Body))
        {
            return Result<OrderNotification>.Invalid(new List<ValidationError> { new() { ErrorMessage = "The original message content is unavailable, so it cannot be resent." } });
        }

        var resend = new OrderNotification(original.OrderId, original.BuyerId, original.Kind, original.ToPhoneNumber, original.Body);
        resend.SetResendProvenance(idempotencyKey, original.Id);
        await SubmitAsync(resend, original.Body, cancellationToken);
        await _notifications.AddAsync(resend, cancellationToken);

        return Result<OrderNotification>.Success(resend);
    }

    public async Task<Result> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            return Result.NotFound();
        }
        if (notification.ContentDisposed)
        {
            return Result.Success();
        }

        // Dispose of the text at the provider so it is no longer retrievable there. Only if that
        // succeeds do we clear it locally and mark it disposed — the record itself survives.
        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            try
            {
                await _twilio.RedactMessageBodyAsync(notification.ProviderMessageSid!, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Content disposal failed to redact message at the provider (notification {0}): {1}", notificationId, ex.Message);
                return Result.Error("Failed to dispose of the message content at the provider.");
            }
        }

        notification.MarkContentDisposed();
        await _notifications.UpdateAsync(notification, cancellationToken);
        return Result.Success();
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var fromNumber = _settings.FromNumber;

        // Ask the provider for this number's messages in range (sender filter applied provider-side).
        var providerMessages = await _twilio.ListMessagesFromNumberAsync(fromNumber, from, to, cancellationToken);

        // What eShop believes it sent in range: notifications that reached the provider (have a SID)
        // whose send/creation time falls within the range.
        var allNotifications = await _notifications.ListAsync(cancellationToken);
        var eShopInRange = allNotifications
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .Where(n =>
            {
                var stamp = n.ProviderDateSent ?? n.CreatedDate;
                return stamp >= from && stamp <= to;
            })
            .ToList();

        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid))
            .GroupBy(m => m.Sid!)
            .ToDictionary(g => g.Key, g => g.First());
        var eShopBySid = eShopInRange
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();
        var eShopOnly = new List<ReconciliationEntry>();

        foreach (var (sid, message) in providerBySid)
        {
            if (eShopBySid.TryGetValue(sid, out var n))
            {
                matched.Add(new ReconciliationEntry(sid, n.Id, n.OrderId, message.Status, n.ProviderStatus, message.DateSent ?? n.ProviderDateSent));
            }
            else
            {
                providerOnly.Add(new ReconciliationEntry(sid, null, null, message.Status, null, message.DateSent));
            }
        }

        foreach (var (sid, n) in eShopBySid)
        {
            if (!providerBySid.ContainsKey(sid))
            {
                eShopOnly.Add(new ReconciliationEntry(sid, n.Id, n.OrderId, null, n.ProviderStatus, n.ProviderDateSent));
            }
        }

        return new ReconciliationReport(from, to, fromNumber, providerBySid.Count, eShopBySid.Count, matched, providerOnly, eShopOnly);
    }

    // ---- helpers -------------------------------------------------------------------------------

    private async Task<ContactNumber?> ResolveDestinationAsync(string buyerId, CancellationToken cancellationToken)
    {
        var owned = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        // Newest-first by the specification; the most recently registered number is used.
        return owned.FirstOrDefault();
    }

    private async Task SendImmediateAsync(Order order, NotificationKind kind, string body, CancellationToken cancellationToken)
    {
        var destination = await ResolveDestinationAsync(order.BuyerId, cancellationToken);
        if (destination is null)
        {
            // A shopper with no number on file is simply not messaged.
            return;
        }

        var notification = new OrderNotification(order.Id, order.BuyerId, kind, destination.PhoneNumber, body);
        await SubmitAsync(notification, body, cancellationToken);
        await _notifications.AddAsync(notification, cancellationToken);
    }

    /// <summary>Submit an immediate message for an already-constructed notification, recording the outcome. Never throws.</summary>
    private async Task SubmitAsync(OrderNotification notification, string body, CancellationToken cancellationToken)
    {
        try
        {
            var message = await _twilio.SendSmsAsync(notification.ToPhoneNumber, body, cancellationToken);
            notification.RecordProviderResult(message.Sid, message.Status ?? NotificationStatuses.Unknown, message.ErrorCode, message.ErrorMessage, isScheduled: false, message.DateSent);
        }
        catch (TwilioApiException ex)
        {
            notification.RecordSubmissionFailure(ex.Message, ex.TwilioCode);
            _logger.LogWarning("Notification submission rejected by provider (order {0}, kind {1}): {2}", notification.OrderId, notification.Kind, ex.Message);
        }
        catch (Exception ex)
        {
            notification.RecordSubmissionFailure("The provider request failed.");
            _logger.LogWarning("Notification submission failed (order {0}, kind {1}): {2}", notification.OrderId, notification.Kind, ex.Message);
        }
    }

    private async Task ScheduleFollowUpAsync(Order order, CancellationToken cancellationToken)
    {
        var destination = await ResolveDestinationAsync(order.BuyerId, cancellationToken);
        if (destination is null)
        {
            return;
        }
        if (string.IsNullOrEmpty(_settings.MessagingServiceSid))
        {
            _logger.LogWarning("No MessagingServiceSid configured; skipping scheduled follow-up for order {0}.", order.Id);
            return;
        }

        var body = $"How did the delivery of your eShop order #{order.Id} go? We'd love your feedback.";
        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        var notification = new OrderNotification(order.Id, order.BuyerId, NotificationKind.DeliveryFollowUp, destination.PhoneNumber, body);
        try
        {
            var message = await _twilio.ScheduleSmsAsync(destination.PhoneNumber, body, sendAt, cancellationToken);
            notification.RecordProviderResult(message.Sid, message.Status ?? NotificationStatuses.Scheduled, message.ErrorCode, message.ErrorMessage, isScheduled: true, message.DateSent);
        }
        catch (TwilioApiException ex)
        {
            notification.RecordSubmissionFailure(ex.Message, ex.TwilioCode);
            _logger.LogWarning("Follow-up scheduling rejected by provider (order {0}): {1}", order.Id, ex.Message);
        }
        catch (Exception ex)
        {
            notification.RecordSubmissionFailure("The provider request failed.");
            _logger.LogWarning("Follow-up scheduling failed (order {0}): {1}", order.Id, ex.Message);
        }
        await _notifications.AddAsync(notification, cancellationToken);
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderSpecification(orderId), cancellationToken);
        foreach (var notification in notifications.Where(n => n.IsScheduled && !string.IsNullOrEmpty(n.ProviderMessageSid)))
        {
            try
            {
                var message = await _twilio.CancelScheduledMessageAsync(notification.ProviderMessageSid!, cancellationToken);
                notification.MarkCancelled(message.Status ?? NotificationStatuses.Canceled);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to cancel scheduled follow-up (order {0}): {1}", orderId, ex.Message);
                // Best-effort: mark our intent so the outcome is visible even if the provider call failed.
                notification.MarkCancelled(NotificationStatuses.Canceled);
            }
            await _notifications.UpdateAsync(notification, cancellationToken);
        }
    }

    /// <summary>Refresh cached delivery outcomes from the provider for any non-terminal message. Never throws.</summary>
    private async Task RefreshOutcomesAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid) || TerminalStatuses.Contains(notification.ProviderStatus))
            {
                continue;
            }
            try
            {
                var message = await _twilio.FetchMessageAsync(notification.ProviderMessageSid!, cancellationToken);
                notification.UpdateDeliveryOutcome(message.Status ?? notification.ProviderStatus, message.ErrorCode, message.ErrorMessage, message.DateSent);
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to refresh delivery outcome for notification {0}: {1}", notification.Id, ex.Message);
            }
        }
    }
}
