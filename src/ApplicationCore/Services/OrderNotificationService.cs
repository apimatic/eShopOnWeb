using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Sms;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates SMS notifications as an order moves. Sending is always best-effort: a message
/// that cannot be handed to the provider is recorded as failed and never propagates out to fail
/// the order operation itself. A shopper with no number on file is simply not messaged.
/// </summary>
public class OrderNotificationService : IOrderNotificationService
{
    /// <summary>How long after dispatch the "how did delivery go?" follow-up is scheduled for.</summary>
    private static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _catalogItemRepository;
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IRepository<NotificationContentDisposal> _disposalRepository;
    private readonly ISmsSender _sms;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> catalogItemRepository,
        IRepository<ContactNumber> contactNumberRepository,
        IRepository<OrderNotification> notificationRepository,
        IRepository<NotificationContentDisposal> disposalRepository,
        ISmsSender sms,
        IUriComposer uriComposer,
        IAppLogger<OrderNotificationService> logger)
    {
        _orderRepository = orderRepository;
        _catalogItemRepository = catalogItemRepository;
        _contactNumberRepository = contactNumberRepository;
        _notificationRepository = notificationRepository;
        _disposalRepository = disposalRepository;
        _sms = sms;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineItem> lines, Address shipToAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(shipToAddress, nameof(shipToAddress));
        if (lines is null || lines.Count == 0)
        {
            throw new ArgumentException("An order must contain at least one item.", nameof(lines));
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);

        var items = new List<OrderItem>();
        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new ArgumentException($"Quantity for catalog item {line.CatalogItemId} must be greater than zero.", nameof(lines));
            }

            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId);
            if (catalogItem is null)
            {
                throw new ArgumentException($"Catalog item {line.CatalogItemId} does not exist.", nameof(lines));
            }

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            items.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, shipToAddress, items);
        order = await _orderRepository.AddAsync(order, cancellationToken);

        await SendToAllContactNumbersAsync(order.Id, buyerId, NotificationKind.OrderPlaced, OrderPlacedBody(order.Id), scheduleFollowUp: false, cancellationToken);

        return order;

    }

    public async Task<bool> NotifyDispatchedAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            return false;
        }

        var existing = await _notificationRepository.ListAsync(new OrderNotificationsByOrderSpecification(orderId), cancellationToken);

        // A cancelled order must not be told it is on its way, and we do not re-dispatch.
        if (existing.Any(n => n.Kind == NotificationKind.Cancelled) || existing.Any(n => n.Kind == NotificationKind.Dispatched))
        {
            return true;
        }

        await SendToAllContactNumbersAsync(order.Id, order.BuyerId, NotificationKind.Dispatched, OrderDispatchedBody(orderId), scheduleFollowUp: true, cancellationToken);
        return true;
    }

    public async Task<bool> NotifyCancelledAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            return false;
        }

        var existing = await _notificationRepository.ListAsync(new OrderNotificationsByOrderSpecification(orderId), cancellationToken);

        // Cancelling is idempotent: if the order was already cancelled, do not call off follow-ups
        // again (they are already off) nor tell the shopper twice.
        if (existing.Any(n => n.Kind == NotificationKind.Cancelled))
        {
            return true;
        }

        // Call off any follow-up that has not gone out yet: a customer must never be asked how the
        // delivery went for an order that was cancelled.
        foreach (var followUp in existing.Where(n => n.Kind == NotificationKind.DeliveryFollowUp && n.Status == NotificationStatuses.Scheduled && n.ProviderMessageId is not null))
        {
            try
            {
                await _sms.CancelScheduledAsync(followUp.ProviderMessageId!, cancellationToken);
                followUp.MarkCanceled();
                await _notificationRepository.UpdateAsync(followUp, cancellationToken);
            }
            catch (Exception ex)
            {
                // If the provider cannot cancel it, surface it: the follow-up may still go out.
                _logger.LogWarning("Failed to cancel scheduled follow-up notification {NotificationId}: {Error}", followUp.Id, ex.Message);
                throw;
            }
        }

        await SendToAllContactNumbersAsync(order.Id, order.BuyerId, NotificationKind.Cancelled, OrderCancelledBody(orderId), scheduleFollowUp: false, cancellationToken);
        return true;
    }

    public async Task<OrderNotification?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        // Idempotency: a repeat under the same key returns the message already produced.
        var alreadyProduced = await _notificationRepository.FirstOrDefaultAsync(new OrderNotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (alreadyProduced is not null)
        {
            return alreadyProduced;
        }

        var source = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (source is null || source.ToNumber is null)
        {
            return null;
        }

        var resend = new OrderNotification(source.OrderId, source.OwnerId, source.Kind);
        resend.SetIdempotencyKey(idempotencyKey);
        var body = BodyFor(source.Kind, source.OrderId);
        resend.SetContent(source.ToNumber, body);

        // Never send to a number that is no longer on file for the shopper.
        var currentNumbers = await _contactNumberRepository.ListAsync(new ContactNumbersByOwnerSpecification(source.OwnerId), cancellationToken);
        var stillRegistered = currentNumbers.Any(c => c.PhoneNumber == source.ToNumber);
        if (!stillRegistered)
        {
            resend.MarkSendFailed("Destination is no longer a registered contact number; nothing was sent.");
        }
        else
        {
            try
            {
                var result = await _sms.SendAsync(source.ToNumber, body, cancellationToken);
                resend.MarkAccepted(result.ProviderMessageId, result.Status);
            }
            catch (Exception ex)
            {
                resend.MarkSendFailed(ex.Message);
                _logger.LogWarning("Re-send of notification {NotificationId} could not be handed to the provider: {Error}", notificationId, ex.Message);
            }
        }

        resend = await _notificationRepository.AddAsync(resend, cancellationToken);
        return resend;
    }

    public async Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.FirstOrDefaultAsync(new OrderNotificationByIdSpecification(notificationId), cancellationToken);
        if (notification is null)
        {
            return false;
        }

        // Dispose of the text at the provider first, so it is no longer retrievable there either.
        if (notification.ProviderMessageId is not null)
        {
            await _sms.RedactAsync(notification.ProviderMessageId, cancellationToken);
        }

        // Record the disposal as its own append-only marker. Reads reflect it back onto the
        // notification, while the notification's record (sent, and its outcome) is left intact.
        var alreadyDisposed = await _disposalRepository.FirstOrDefaultAsync(new ContentDisposalByNotificationSpecification(notificationId), cancellationToken);
        if (alreadyDisposed is null)
        {
            await _disposalRepository.AddAsync(new NotificationContentDisposal(notificationId), cancellationToken);
        }

        return true;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var providerMessages = await _sms.ListSentMessagesAsync(from, to, cancellationToken);
        var providerById = providerMessages
            .GroupBy(m => m.ProviderMessageId)
            .ToDictionary(g => g.Key, g => g.First());

        var allNotifications = await _notificationRepository.ListAsync(cancellationToken);
        var eShopInRange = allNotifications.Where(n => n.CreatedAt >= from && n.CreatedAt <= to).ToList();

        var matched = new List<ReconciliationMatch>();
        var eShopOnly = new List<EShopOnlyNotification>();
        var matchedProviderIds = new HashSet<string>();

        foreach (var notification in eShopInRange)
        {
            if (notification.ProviderMessageId is not null && providerById.TryGetValue(notification.ProviderMessageId, out var providerMessage))
            {
                matched.Add(new ReconciliationMatch(
                    notification.ProviderMessageId,
                    providerMessage.Status,
                    notification.Id,
                    notification.OrderId,
                    notification.Status));
                matchedProviderIds.Add(notification.ProviderMessageId);
            }
            else
            {
                eShopOnly.Add(new EShopOnlyNotification(
                    notification.Id,
                    notification.OrderId,
                    notification.ProviderMessageId,
                    notification.Status));
            }
        }

        var providerOnly = providerMessages
            .Where(m => !matchedProviderIds.Contains(m.ProviderMessageId))
            .Select(m => new ProviderOnlyMessage(m.ProviderMessageId, m.Status, m.DateSent))
            .ToList();

        return new ReconciliationReport(from, to, matched, providerOnly, eShopOnly);
    }

    public async Task<IReadOnlyList<OrderNotification>?> GetOrderNotificationsAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByOrderSpecification(orderId), cancellationToken);
        await RefreshDeliveryStateAsync(notifications, cancellationToken);
        await ApplyContentDisposalsAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<IReadOnlyList<OrderNotification>> RefreshOwnerNotificationsAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByOwnerSpecification(ownerId), cancellationToken);
        await RefreshDeliveryStateAsync(notifications, cancellationToken);
        await ApplyContentDisposalsAsync(notifications, cancellationToken);
        return notifications;
    }

    // Reflect append-only disposal markers back onto the notifications for the response. This is
    // an in-memory projection for reads only; nothing is persisted here.
    private async Task ApplyContentDisposalsAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        if (notifications.Count == 0)
        {
            return;
        }

        var disposals = await _disposalRepository.ListAsync(
            new ContentDisposalsForNotificationsSpecification(notifications.Select(n => n.Id)),
            cancellationToken);
        if (disposals.Count == 0)
        {
            return;
        }

        var disposedIds = disposals.Select(d => d.NotificationId).ToHashSet();
        foreach (var notification in notifications)
        {
            if (disposedIds.Contains(notification.Id))
            {
                notification.RedactContent();
            }
        }
    }

    // Pull the current delivery outcome from the provider for any message that is not yet in a
    // terminal state. Read-time refresh is how the app learns outcomes given it cannot be called
    // back. This refreshes the returned objects for the response but is deliberately side-effect
    // free: reads never write. (It also keeps a read from persisting a change and, under the
    // in-memory provider, interfering with a later operation's write to the same row.)
    private async Task RefreshDeliveryStateAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (notification.ProviderMessageId is null || NotificationStatuses.IsTerminal(notification.Status))
            {
                continue;
            }

            try
            {
                var state = await _sms.GetDeliveryStateAsync(notification.ProviderMessageId, cancellationToken);
                notification.UpdateDeliveryState(state.Status, state.ErrorCode, state.ErrorMessage);
            }
            catch (Exception ex)
            {
                // A failure to refresh must never fail the read.
                _logger.LogWarning("Could not refresh delivery state for notification {NotificationId}: {Error}", notification.Id, ex.Message);
            }
        }
    }

    private async Task SendToAllContactNumbersAsync(int orderId, string ownerId, NotificationKind kind, string body, bool scheduleFollowUp, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumberRepository.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), cancellationToken);
        if (numbers.Count == 0)
        {
            // A shopper with no number on file is simply not messaged.
            return;
        }

        foreach (var contactNumber in numbers)
        {
            await SendOneAsync(orderId, ownerId, kind, body, contactNumber.PhoneNumber, cancellationToken);

            if (scheduleFollowUp)
            {
                await ScheduleFollowUpAsync(orderId, ownerId, contactNumber.PhoneNumber, cancellationToken);
            }
        }
    }

    private async Task SendOneAsync(int orderId, string ownerId, NotificationKind kind, string body, string toNumber, CancellationToken cancellationToken)
    {
        var notification = new OrderNotification(orderId, ownerId, kind);
        notification.SetContent(toNumber, body);

        try
        {
            var result = await _sms.SendAsync(toNumber, body, cancellationToken);
            notification.MarkAccepted(result.ProviderMessageId, result.Status);
        }
        catch (Exception ex)
        {
            // Best-effort: record the failure but never fail the order operation.
            notification.MarkSendFailed(ex.Message);
            _logger.LogWarning("Order {OrderId} {Kind} notification could not be handed to the provider: {Error}", orderId, kind, ex.Message);
        }

        await _notificationRepository.AddAsync(notification, cancellationToken);
    }

    private async Task ScheduleFollowUpAsync(int orderId, string ownerId, string toNumber, CancellationToken cancellationToken)
    {
        var followUp = new OrderNotification(orderId, ownerId, NotificationKind.DeliveryFollowUp);
        var body = DeliveryFollowUpBody(orderId);
        var sendAt = DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay);
        followUp.SetContent(toNumber, body);

        try
        {
            var result = await _sms.ScheduleAsync(toNumber, body, sendAt, cancellationToken);
            followUp.MarkScheduled(result.ProviderMessageId, result.Status, sendAt);
        }
        catch (Exception ex)
        {
            followUp.MarkSendFailed(ex.Message);
            _logger.LogWarning("Order {OrderId} delivery follow-up could not be scheduled with the provider: {Error}", orderId, ex.Message);
        }

        await _notificationRepository.AddAsync(followUp, cancellationToken);
    }

    private static string BodyFor(NotificationKind kind, int orderId) => kind switch
    {
        NotificationKind.OrderPlaced => OrderPlacedBody(orderId),
        NotificationKind.Dispatched => OrderDispatchedBody(orderId),
        NotificationKind.DeliveryFollowUp => DeliveryFollowUpBody(orderId),
        NotificationKind.Cancelled => OrderCancelledBody(orderId),
        _ => $"eShop: an update about your order #{orderId}."
    };

    private static string OrderPlacedBody(int orderId) => $"eShop: your order #{orderId} has been placed. Thank you for shopping with us!";
    private static string OrderDispatchedBody(int orderId) => $"eShop: good news - your order #{orderId} is on its way!";
    private static string DeliveryFollowUpBody(int orderId) => $"eShop: how did the delivery of your order #{orderId} go? We'd love your feedback.";
    private static string OrderCancelledBody(int orderId) => $"eShop: your order #{orderId} has been cancelled. If this is unexpected, please contact support.";
}
