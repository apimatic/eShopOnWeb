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
/// Orchestrates orders and the SMS notifications that go out as they move. Every send is
/// best-effort: a failed message is recorded but never fails the order operation, and a shopper
/// with no number on file is simply not messaged. Destination numbers are never logged.
/// </summary>
public class OrderNotificationService : IOrderNotificationService
{
    /// <summary>How far ahead the "how did delivery go?" follow-up is queued with the provider.</summary>
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    /// <summary>Local sentinel status for a send that never reached the provider (no SID).</summary>
    private const string SendFailedStatus = "send_failed";

    /// <summary>Provider statuses that will not change further, so refresh/cancel can skip them.</summary>
    private static readonly HashSet<string> TerminalStatuses =
        new(StringComparer.OrdinalIgnoreCase) { "delivered", "undelivered", "failed", "canceled", "read" };

    // Orders have a required shipping address; the notification API places from catalog items only,
    // so a clearly-marked placeholder is stored rather than inventing shopper address data. A fresh
    // instance is created per order — an owned value object must never be shared across aggregates.
    private static Address CreatePlaceholderAddress() =>
        new("Not provided", "Not provided", "", "Not provided", "00000");

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<ContactNumber> _contactRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly ISmsGateway _smsGateway;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<ContactNumber> contactRepository,
        IRepository<OrderNotification> notificationRepository,
        ISmsGateway smsGateway,
        IUriComposer uriComposer,
        IAppLogger<OrderNotificationService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _contactRepository = contactRepository;
        _notificationRepository = notificationRepository;
        _smsGateway = smsGateway;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineSelection> lines, CancellationToken ct)
    {
        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), ct);

        var items = new List<OrderItem>();
        foreach (var line in lines)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw new CatalogItemNotFoundException(line.CatalogItemId);

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            items.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, CreatePlaceholderAddress(), items);
        await _orderRepository.AddAsync(order, ct);

        await NotifyAsync(order, NotificationKind.OrderPlaced,
            $"Your eShop order #{order.Id} has been placed. Thank you for shopping with us!", schedule: false, ct);

        return order;
    }

    public async Task<Order?> DispatchOrderAsync(int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, ct);
        if (order is null)
            return null;

        order.MarkDispatched();
        await _orderRepository.UpdateAsync(order, ct);

        await NotifyAsync(order, NotificationKind.OrderDispatched,
            $"Good news! Your eShop order #{order.Id} is on its way.", schedule: false, ct);

        // Queue the delivery-feedback follow-up with the provider for a few days later.
        await NotifyAsync(order, NotificationKind.DeliveryFeedback,
            $"How did the delivery of your eShop order #{order.Id} go? We'd love your feedback.", schedule: true, ct);

        return order;
    }

    public async Task<Order?> CancelOrderAsync(int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, ct);
        if (order is null)
            return null;

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, ct);

        // Call off any follow-up that has not yet gone out — a cancelled order must never get the
        // "how did delivery go?" message.
        await CancelPendingFollowUpsAsync(order.Id, ct);

        await NotifyAsync(order, NotificationKind.OrderCancelled,
            $"Your eShop order #{order.Id} has been cancelled.", schedule: false, ct);

        return order;
    }

    public async Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(int orderId, CancellationToken ct)
    {
        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByOrderSpecification(orderId), ct);
        await RefreshStatusesAsync(notifications, ct);
        return notifications;
    }

    public async Task<IReadOnlyList<OrderNotification>> GetBuyerNotificationsAsync(string buyerId, CancellationToken ct)
    {
        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByBuyerSpecification(buyerId), ct);
        await RefreshStatusesAsync(notifications, ct);
        return notifications;
    }

    public async Task<OrderNotification?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct)
    {
        // Idempotency: a repeat under the same key returns the first result and sends nothing new.
        var alreadyDone = await _notificationRepository.FirstOrDefaultAsync(
            new OrderNotificationByIdempotencyKeySpecification(idempotencyKey), ct);
        if (alreadyDone is not null)
            return alreadyDone;

        var original = await _notificationRepository.GetByIdAsync(notificationId, ct);
        if (original is null)
            return null;

        var body = original.Body ?? $"An update regarding your eShop order #{original.OrderId}.";

        var resend = new OrderNotification(original.OrderId, original.BuyerId, original.Kind, original.ToNumber, body, isScheduled: false);
        resend.AttachIdempotencyKey(idempotencyKey);

        try
        {
            var result = await _smsGateway.SendAsync(original.ToNumber, body, ct);
            resend.RecordProviderResult(result.Sid, result.Status, result.ErrorCode, result.ErrorMessage);
        }
        catch (SmsGatewayException ex)
        {
            resend.RecordSendFailure(SendFailedStatus, ex.Message);
            _logger.LogWarning("Resend for order {0} could not be delivered to the provider: {1}", original.OrderId, ex.Message);
        }

        await _notificationRepository.AddAsync(resend, ct);
        return resend;
    }

    public async Task<OrderNotification?> RedactContentAsync(int notificationId, CancellationToken ct)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, ct);
        if (notification is null)
            return null;

        // The content must be gone at the provider too, not merely hidden here. Only mark it redacted
        // locally once the provider has confirmed — a provider failure propagates to the caller.
        if (notification.ProviderMessageSid is not null)
        {
            await _smsGateway.RedactAsync(notification.ProviderMessageSid, ct);
        }

        notification.MarkContentRedacted();
        await _notificationRepository.UpdateAsync(notification, ct);
        return notification;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        // Ask the provider directly for only our sending number's traffic over the range.
        var providerMessages = await _smsGateway.ListSentMessagesAsync(from, to, ct);
        var localNotifications = await _notificationRepository.ListAsync(new OrderNotificationsSentInRangeSpecification(from, to), ct);

        var localBySid = localNotifications
            .Where(n => n.ProviderMessageSid is not null)
            .GroupBy(n => n.ProviderMessageSid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var providerSids = new HashSet<string>(
            providerMessages.Where(m => m.Sid is not null).Select(m => m.Sid!), StringComparer.Ordinal);

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();

        foreach (var message in providerMessages)
        {
            if (message.Sid is not null && localBySid.TryGetValue(message.Sid, out var local))
            {
                matched.Add(new ReconciliationEntry(message.Sid, local.Id, local.OrderId, message.Status, local.Status, message.DateSent));
            }
            else
            {
                // The provider knows about a message eShop has no record of.
                providerOnly.Add(new ReconciliationEntry(message.Sid, null, null, message.Status, null, message.DateSent));
            }
        }

        // eShop believes it sent a message the provider doesn't return for this number/range.
        var eShopOnly = localNotifications
            .Where(n => n.ProviderMessageSid is not null && !providerSids.Contains(n.ProviderMessageSid!))
            .Select(n => new ReconciliationEntry(n.ProviderMessageSid, n.Id, n.OrderId, null, n.Status, null))
            .ToList();

        return new ReconciliationReport(from, to, providerMessages.Count, localNotifications.Count, matched, providerOnly, eShopOnly);
    }

    private async Task NotifyAsync(Order order, NotificationKind kind, string body, bool schedule, CancellationToken ct)
    {
        var numbers = await _contactRepository.ListAsync(new ContactNumbersByBuyerSpecification(order.BuyerId), ct);
        if (numbers.Count == 0)
        {
            // A shopper with no number on file is simply not messaged.
            _logger.LogInformation("Order {0}: no contact number on file for the buyer; {1} notification skipped.", order.Id, kind);
            return;
        }

        foreach (var contactNumber in numbers)
        {
            var notification = new OrderNotification(order.Id, order.BuyerId, kind, contactNumber.PhoneNumber, body, schedule);

            try
            {
                var result = schedule
                    ? await _smsGateway.ScheduleAsync(contactNumber.PhoneNumber, body, DateTimeOffset.UtcNow.Add(FollowUpDelay), ct)
                    : await _smsGateway.SendAsync(contactNumber.PhoneNumber, body, ct);

                notification.RecordProviderResult(result.Sid, result.Status, result.ErrorCode, result.ErrorMessage);
            }
            catch (SmsGatewayException ex)
            {
                // A message that cannot be sent must never fail the underlying operation.
                notification.RecordSendFailure(SendFailedStatus, ex.Message);
                _logger.LogWarning("Order {0}: {1} notification could not be sent: {2}", order.Id, kind, ex.Message);
            }

            await _notificationRepository.AddAsync(notification, ct);
        }
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken ct)
    {
        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByOrderSpecification(orderId), ct);

        foreach (var notification in notifications.Where(IsCancellableFollowUp))
        {
            try
            {
                var result = await _smsGateway.CancelScheduledAsync(notification.ProviderMessageSid!, ct);
                notification.MarkScheduledCancelled(result.Status ?? "canceled");
                await _notificationRepository.UpdateAsync(notification, ct);
            }
            catch (SmsGatewayException ex)
            {
                _logger.LogWarning("Order {0}: a scheduled follow-up could not be cancelled at the provider: {1}", orderId, ex.Message);
            }
        }
    }

    private async Task RefreshStatusesAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken ct)
    {
        foreach (var notification in notifications)
        {
            if (notification.ProviderMessageSid is null || IsTerminal(notification.Status))
                continue;

            try
            {
                var result = await _smsGateway.FetchAsync(notification.ProviderMessageSid, ct);
                notification.UpdateStatus(result.Status, result.ErrorCode, result.ErrorMessage);
                await _notificationRepository.UpdateAsync(notification, ct);
            }
            catch (SmsGatewayException ex)
            {
                // Best-effort refresh: a provider hiccup must not fail the read.
                _logger.LogWarning("Could not refresh delivery status for notification {0}: {1}", notification.Id, ex.Message);
            }
        }
    }

    private static bool IsCancellableFollowUp(OrderNotification notification) =>
        notification.IsScheduled
        && notification.ProviderMessageSid is not null
        && !IsTerminal(notification.Status);

    private static bool IsTerminal(string? status) =>
        status is not null && TerminalStatuses.Contains(status);
}
