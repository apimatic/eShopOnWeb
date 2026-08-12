using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Messaging;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Coordinates the order lifecycle with the SMS notifications that go out as an order moves.
/// The guiding rule: messaging is best-effort and never fails the underlying operation. The order
/// is committed first; every send is then attempted and recorded — success or failure — so an
/// operator can later reconcile, resend, or dispose of it.
/// </summary>
public class OrderNotificationService : IOrderNotificationService
{
    /// <summary>How long after dispatch the "how did delivery go?" follow-up is queued for.</summary>
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IRepository<Notification> _notificationRepository;
    private readonly ISmsNotificationGateway _gateway;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<ContactNumber> contactNumberRepository,
        IRepository<Notification> notificationRepository,
        ISmsNotificationGateway gateway,
        IUriComposer uriComposer,
        IAppLogger<OrderNotificationService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _contactNumberRepository = contactNumberRepository;
        _notificationRepository = notificationRepository;
        _gateway = gateway;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    // ----- Contact numbers -----

    public async Task<ContactNumber> RegisterContactNumberAsync(string buyerId, string rawNumber)
    {
        var validation = await _gateway.ValidateDestinationAsync(rawNumber);
        if (!validation.IsUsable || string.IsNullOrWhiteSpace(validation.CanonicalNumber))
        {
            // Rejected at registration, not at send time.
            throw new ArgumentException(validation.Reason ?? "The phone number is not a usable destination.", nameof(rawNumber));
        }

        var contactNumber = new ContactNumber(buyerId, validation.CanonicalNumber);
        await _contactNumberRepository.AddAsync(contactNumber);
        _logger.LogInformation("Registered contact number {ContactNumberId} for a shopper.", contactNumber.Id);
        return contactNumber;
    }

    public async Task<IReadOnlyList<ContactNumber>> ListContactNumbersAsync(string buyerId)
    {
        var numbers = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId));
        return numbers;
    }

    public async Task<bool> DeleteContactNumberAsync(string buyerId, int contactNumberId)
    {
        var contactNumber = await _contactNumberRepository.GetByIdAsync(contactNumberId);
        // A number belongs to the shopper who registered it: one shopper must never delete another's.
        if (contactNumber is null || contactNumber.BuyerId != buyerId)
        {
            return false;
        }
        await _contactNumberRepository.DeleteAsync(contactNumber);
        _logger.LogInformation("Removed contact number {ContactNumberId} for a shopper.", contactNumberId);
        return true;
    }

    // ----- Orders -----

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines)
    {
        if (lines is null || lines.Count == 0)
        {
            throw new ArgumentException("An order must contain at least one line.", nameof(lines));
        }
        if (lines.Any(l => l.Units <= 0))
        {
            throw new ArgumentException("Every order line must have a quantity of at least one.", nameof(lines));
        }

        var catalogItemIds = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(catalogItemIds));
        var missing = catalogItemIds.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
        {
            throw new ArgumentException($"Unknown catalog item id(s): {string.Join(", ", missing)}.", nameof(lines));
        }

        var orderItems = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Units);
        }).ToList();

        // API checkout carries no shipping address; reuse the existing model with a placeholder.
        var shipToAddress = new Address("N/A", "N/A", "N/A", "N/A", "N/A");
        var order = new Order(buyerId, shipToAddress, orderItems);

        // Commit the order before messaging so a send failure can never undo it.
        await _orderRepository.AddAsync(order);
        _logger.LogInformation("Placed order {OrderId}.", order.Id);

        await NotifyAsync(order, NotificationKind.OrderPlaced);
        return order;
    }

    public async Task<Order?> DispatchOrderAsync(int orderId)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId));
        if (order is null)
        {
            return null;
        }

        order.MarkDispatched();
        await _orderRepository.UpdateAsync(order);
        _logger.LogInformation("Dispatched order {OrderId}.", order.Id);

        await NotifyAsync(order, NotificationKind.OrderDispatched);
        await QueueDeliveryFollowUpAsync(order);
        return order;
    }

    public async Task<Order?> CancelOrderAsync(int orderId)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId));
        if (order is null)
        {
            return null;
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order);
        _logger.LogInformation("Cancelled order {OrderId}.", order.Id);

        // Critical safety step first: call off any follow-up that has not gone out, so a cancelled
        // order never triggers a "how did delivery go?" message.
        await CancelPendingFollowUpsAsync(order);
        await NotifyAsync(order, NotificationKind.OrderCancelled);
        return order;
    }

    public async Task<IReadOnlyList<Order>> ListOrdersForBuyerAsync(string buyerId) =>
        await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId));

    public async Task<Order?> GetOrderAsync(int orderId) =>
        await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId));

    public async Task<IReadOnlyList<Notification>> ListNotificationsForOrderAsync(int orderId)
    {
        var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderSpecification(orderId));
        await RefreshOutcomesAsync(notifications);
        return notifications;
    }

    // ----- Operator notification actions -----

    public async Task<Notification?> GetNotificationAsync(int notificationId) =>
        await _notificationRepository.GetByIdAsync(notificationId);

    public async Task<Notification> ResendNotificationAsync(int notificationId, string idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("An idempotency key is required.", nameof(idempotencyKey));
        }

        // Repeating a request under the same key must not send a second message.
        var existing = await _notificationRepository.FirstOrDefaultAsync(new NotificationByIdempotencyKeySpecification(idempotencyKey));
        if (existing is not null)
        {
            _logger.LogInformation("Resend under an existing idempotency key returned notification {NotificationId}.", existing.Id);
            return existing;
        }

        var original = await _notificationRepository.GetByIdAsync(notificationId)
            ?? throw new ArgumentException($"No notification with id {notificationId}.", nameof(notificationId));

        var order = await _orderRepository.GetByIdAsync(original.OrderId);
        var body = BuildBody(original.Kind, original.OrderId, order?.Total());

        var resend = new Notification(original.BuyerId, original.OrderId, original.Kind, original.ToNumber);
        resend.MarkAsResend(original.Id, idempotencyKey);
        try
        {
            var result = await _gateway.SendAsync(original.ToNumber, body);
            resend.RecordProviderResult(result.Sid, result.Status, result.ErrorCode, result.ErrorMessage);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Resend of notification {NotificationId} could not be sent: {Error}", notificationId, ex.Message);
        }

        await _notificationRepository.AddAsync(resend);
        _logger.LogInformation("Resent notification {OriginalId} as {NotificationId}.", original.Id, resend.Id);
        return resend;
    }

    public async Task DisposeNotificationContentAsync(int notificationId)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId)
            ?? throw new ArgumentException($"No notification with id {notificationId}.", nameof(notificationId));

        // Dispose at the provider so the text is no longer retrievable there. If this fails, do not
        // claim success — this action is specifically about the provider's copy.
        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            await _gateway.DisposeContentAsync(notification.ProviderMessageSid);
        }

        notification.MarkContentDisposed();
        await _notificationRepository.UpdateAsync(notification);
        _logger.LogInformation("Disposed of content for notification {NotificationId}.", notificationId);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to)
    {
        var providerMessages = await _gateway.ListSentMessagesAsync(from, to);

        // Provider-side: messages the provider dates as sent within the exact window, keyed by SID.
        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid) && m.DateSent.HasValue && m.DateSent.Value >= from && m.DateSent.Value <= to)
            .GroupBy(m => m.Sid!)
            .ToDictionary(g => g.Key, g => g.First());

        // eShop-side: notifications this app believes it actually sent within the window. Scheduled,
        // cancelled and never-sent notifications are not claims of a sent message.
        var allNotifications = await _notificationRepository.ListAsync();
        var eShopBySid = allNotifications
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid)
                        && n.CreatedAt >= from && n.CreatedAt <= to
                        && n.Status != MessageDeliveryStatus.Scheduled
                        && n.Status != MessageDeliveryStatus.Canceled
                        && n.Status != MessageDeliveryStatus.NotSent)
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();
        var eShopOnly = new List<ReconciliationEntry>();

        foreach (var (sid, providerMessage) in providerBySid)
        {
            if (eShopBySid.TryGetValue(sid, out var notification))
            {
                matched.Add(new ReconciliationEntry
                {
                    Sid = sid,
                    ProviderStatus = providerMessage.Status,
                    EShopStatus = notification.Status,
                    NotificationId = notification.Id,
                    OrderId = notification.OrderId,
                    Kind = notification.Kind.ToString(),
                    DateSent = providerMessage.DateSent
                });
            }
            else
            {
                providerOnly.Add(new ReconciliationEntry
                {
                    Sid = sid,
                    ProviderStatus = providerMessage.Status,
                    DateSent = providerMessage.DateSent
                });
            }
        }

        foreach (var (sid, notification) in eShopBySid)
        {
            if (!providerBySid.ContainsKey(sid))
            {
                eShopOnly.Add(new ReconciliationEntry
                {
                    Sid = sid,
                    EShopStatus = notification.Status,
                    NotificationId = notification.Id,
                    OrderId = notification.OrderId,
                    Kind = notification.Kind.ToString()
                });
            }
        }

        return new ReconciliationReport
        {
            From = from,
            To = to,
            FromNumber = providerMessages.Count > 0 ? (providerMessages[0].From ?? string.Empty) : string.Empty,
            ProviderCount = providerBySid.Count,
            EShopCount = eShopBySid.Count,
            MatchedCount = matched.Count,
            ProviderOnlyCount = providerOnly.Count,
            EShopOnlyCount = eShopOnly.Count,
            Matched = matched,
            ProviderOnly = providerOnly,
            EShopOnly = eShopOnly
        };
    }

    // ----- Internals -----

    private async Task NotifyAsync(Order order, NotificationKind kind)
    {
        var numbers = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(order.BuyerId));
        // A shopper with no number on file is simply not messaged.
        foreach (var contact in numbers)
        {
            var body = BuildBody(kind, order.Id, order.Total());
            var notification = new Notification(order.BuyerId, order.Id, kind, contact.PhoneNumber);
            try
            {
                var result = await _gateway.SendAsync(contact.PhoneNumber, body);
                notification.RecordProviderResult(result.Sid, result.Status, result.ErrorCode, result.ErrorMessage);
            }
            catch (Exception ex)
            {
                // A message that cannot be sent must never fail the operation.
                _logger.LogWarning("Order {OrderId}: {Kind} notification could not be sent: {Error}", order.Id, kind, ex.Message);
            }
            await _notificationRepository.AddAsync(notification);
        }
    }

    private async Task QueueDeliveryFollowUpAsync(Order order)
    {
        var numbers = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(order.BuyerId));
        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        foreach (var contact in numbers)
        {
            var body = BuildBody(NotificationKind.DeliveryFollowUp, order.Id, order.Total());
            var notification = new Notification(order.BuyerId, order.Id, NotificationKind.DeliveryFollowUp, contact.PhoneNumber);
            try
            {
                var result = await _gateway.ScheduleAsync(contact.PhoneNumber, body, sendAt);
                notification.RecordProviderResult(result.Sid, result.Status, result.ErrorCode, result.ErrorMessage, result.ScheduledFor ?? sendAt);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Order {OrderId}: delivery follow-up could not be queued: {Error}", order.Id, ex.Message);
            }
            await _notificationRepository.AddAsync(notification);
        }
    }

    private async Task CancelPendingFollowUpsAsync(Order order)
    {
        var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderSpecification(order.Id));
        foreach (var notification in notifications)
        {
            var isPendingFollowUp = notification.Kind == NotificationKind.DeliveryFollowUp
                && !string.IsNullOrEmpty(notification.ProviderMessageSid)
                && notification.Status == MessageDeliveryStatus.Scheduled;
            if (!isPendingFollowUp)
            {
                continue;
            }
            try
            {
                await _gateway.CancelScheduledAsync(notification.ProviderMessageSid!);
                notification.UpdateDeliveryOutcome(MessageDeliveryStatus.Canceled, null, null);
                await _notificationRepository.UpdateAsync(notification);
                _logger.LogInformation("Called off scheduled follow-up {NotificationId} for cancelled order {OrderId}.", notification.Id, order.Id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Order {OrderId}: could not call off follow-up {NotificationId}: {Error}", order.Id, notification.Id, ex.Message);
            }
        }
    }

    private async Task RefreshOutcomesAsync(IReadOnlyList<Notification> notifications)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid) || MessageDeliveryStatus.IsTerminal(notification.Status))
            {
                continue;
            }
            try
            {
                var latest = await _gateway.FetchAsync(notification.ProviderMessageSid!);
                notification.UpdateDeliveryOutcome(latest.Status, latest.ErrorCode, latest.ErrorMessage);
                await _notificationRepository.UpdateAsync(notification);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not refresh outcome for notification {NotificationId}: {Error}", notification.Id, ex.Message);
            }
        }
    }

    private static string BuildBody(NotificationKind kind, int orderId, decimal? total) => kind switch
    {
        NotificationKind.OrderPlaced => total.HasValue
            ? $"eShop: your order #{orderId} has been placed. Total {total.Value.ToString("0.00", CultureInfo.InvariantCulture)}. Thank you for shopping with us!"
            : $"eShop: your order #{orderId} has been placed. Thank you for shopping with us!",
        NotificationKind.OrderDispatched => $"eShop: good news! Your order #{orderId} is on its way.",
        NotificationKind.DeliveryFollowUp => $"eShop: how did the delivery of your order #{orderId} go? We would love your feedback.",
        NotificationKind.OrderCancelled => $"eShop: your order #{orderId} has been cancelled. If this is unexpected, please contact us.",
        _ => $"eShop: an update about your order #{orderId}."
    };
}
