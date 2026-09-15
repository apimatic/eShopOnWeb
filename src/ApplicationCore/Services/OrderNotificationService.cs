using System;
using System.Collections.Generic;
using System.Linq;
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
    // Delivery outcomes from which a message will not move again.
    private static readonly HashSet<string> TerminalStatuses =
        new(StringComparer.OrdinalIgnoreCase) { "delivered", "undelivered", "failed", "canceled", OrderNotification.NotSentStatus };

    // Statuses that mean eShop handed the message to the provider to go out now (not merely scheduled).
    private static readonly HashSet<string> SentStatuses =
        new(StringComparer.OrdinalIgnoreCase) { "queued", "sending", "sent", "delivered", "undelivered", "failed", "accepted" };

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<ContactNumber> _contactRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly ISmsProvider _smsProvider;
    private readonly IUriComposer _uriComposer;
    private readonly NotificationSettings _settings;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<ContactNumber> contactRepository,
        IRepository<OrderNotification> notificationRepository,
        ISmsProvider smsProvider,
        IUriComposer uriComposer,
        NotificationSettings settings,
        IAppLogger<OrderNotificationService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _contactRepository = contactRepository;
        _notificationRepository = notificationRepository;
        _smsProvider = smsProvider;
        _uriComposer = uriComposer;
        _settings = settings;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines, Address shipToAddress)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
        {
            throw new OrderRequestException("An order must contain at least one item.");
        }

        var catalogItemIds = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(catalogItemIds));

        var orderItems = new List<OrderItem>();
        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new OrderRequestException($"Quantity for catalog item {line.CatalogItemId} must be greater than zero.");
            }

            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId);
            if (catalogItem is null)
            {
                throw new OrderRequestException($"Catalog item {line.CatalogItemId} does not exist.");
            }

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, shipToAddress, orderItems);
        await _orderRepository.AddAsync(order);

        _logger.LogInformation("Placed order {OrderId} for buyer {BuyerId}.", order.Id, buyerId);

        await NotifyOwnerAsync(order, NotificationKind.OrderPlaced, buyerId);
        return order;
    }

    public async Task<Order> DispatchAsync(int orderId)
    {
        var order = await _orderRepository.GetByIdAsync(orderId) ?? throw new OrderNotFoundException(orderId);

        order.MarkDispatched();
        await _orderRepository.UpdateAsync(order);
        _logger.LogInformation("Dispatched order {OrderId}.", orderId);

        // Tell the shopper it is on its way...
        await NotifyOwnerAsync(order, NotificationKind.OrderDispatched, order.BuyerId);

        // ...and queue the "how did delivery go?" follow-up with the provider for a few days later.
        var sendAt = DateTimeOffset.UtcNow.AddDays(_settings.FollowUpDelayDays);
        await NotifyOwnerAsync(order, NotificationKind.DeliveryFollowUp, order.BuyerId, scheduledSendAt: sendAt);

        return order;
    }

    public async Task<Order> CancelAsync(int orderId)
    {
        var order = await _orderRepository.GetByIdAsync(orderId) ?? throw new OrderNotFoundException(orderId);

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order);
        _logger.LogInformation("Cancelled order {OrderId}.", orderId);

        // A follow-up that has not yet gone out must never reach the shopper: call off every scheduled one.
        await CancelPendingFollowUpsAsync(orderId);

        // Tell the shopper the order was cancelled.
        await NotifyOwnerAsync(order, NotificationKind.OrderCancelled, order.BuyerId);

        return order;
    }

    private async Task CancelPendingFollowUpsAsync(int orderId)
    {
        var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderSpecification(orderId));
        foreach (var n in notifications.Where(n =>
                     n.Kind == NotificationKind.DeliveryFollowUp &&
                     n.ProviderMessageSid != null &&
                     !TerminalStatuses.Contains(n.Status) &&
                     !string.Equals(n.Status, "sent", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var pm = await _smsProvider.CancelScheduledAsync(n.ProviderMessageSid!);
                n.UpdateDeliveryState(pm.Status, pm.ErrorCode, pm.ErrorMessage);
                await _notificationRepository.UpdateAsync(n);
                _logger.LogInformation("Called off scheduled follow-up notification {NotificationId} for order {OrderId}.", n.Id, orderId);
            }
            catch (SmsProviderException ex)
            {
                // The follow-up may already have left; refresh its state so the record stays truthful.
                _logger.LogWarning("Could not cancel follow-up notification {NotificationId} (provider code {Code}); refreshing state.",
                    n.Id, ex.ProviderErrorCode);
                await TryRefreshAsync(n);
            }
        }
    }

    public async Task<IReadOnlyList<OrderWithNotifications>> GetMyOrdersAsync(string buyerId)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId));
        if (orders.Count == 0)
        {
            return Array.Empty<OrderWithNotifications>();
        }

        var orderIds = orders.Select(o => o.Id).ToArray();
        var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderIdsSpecification(orderIds));
        await RefreshAsync(notifications);

        var byOrder = notifications.ToLookup(n => n.OrderId);
        return orders
            .OrderByDescending(o => o.OrderDate)
            .Select(o => new OrderWithNotifications(o, byOrder[o.Id].ToList()))
            .ToList();
    }

    public async Task<OrderWithNotifications?> GetOrderNotificationsAsync(int orderId, string buyerId)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId));

        // Not found, or not the caller's: report as not found either way so ownership is never revealed.
        if (order is null || order.BuyerId != buyerId)
        {
            return null;
        }

        var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderSpecification(orderId));
        await RefreshAsync(notifications);
        return new OrderWithNotifications(order, notifications);
    }

    public async Task<OrderNotification?> ResendAsync(int notificationId, string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        // Idempotency: a repeat under the same key returns the message the first attempt produced and
        // sends nothing more.
        var priorForKey = await _notificationRepository.ListAsync(new NotificationByIdempotencyKeySpecification(idempotencyKey));
        if (priorForKey.Count > 0)
        {
            _logger.LogInformation("Resend under idempotency key already handled; returning notification {NotificationId}.", priorForKey[0].Id);
            return priorForKey[0];
        }

        var source = await _notificationRepository.GetByIdAsync(notificationId);
        if (source is null)
        {
            return null;
        }

        if (string.IsNullOrEmpty(source.Body))
        {
            throw new ContentAlreadyDisposedException();
        }

        var resend = new OrderNotification(source.OrderId, source.OwnerId, source.Kind, source.ToNumber, source.Body);
        resend.SetResendMetadata(source.Id, idempotencyKey);

        try
        {
            var pm = await _smsProvider.SendAsync(source.ToNumber, source.Body);
            resend.RecordAccepted(pm.Sid, pm.Status ?? string.Empty);
            _logger.LogInformation("Resent notification {SourceId} as {NewId}.", source.Id, resend.Id);
        }
        catch (SmsProviderException ex)
        {
            resend.RecordNotSent(null, ex.ProviderErrorCode);
            _logger.LogWarning("Resend of notification {SourceId} was not accepted by the provider (code {Code}).", source.Id, ex.ProviderErrorCode);
        }

        // The key is stored on the record whether or not the send was accepted, so a repeat never sends again.
        await _notificationRepository.AddAsync(resend);
        return resend;
    }

    public async Task<bool> DisposeContentAsync(int notificationId)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId);
        if (notification is null)
        {
            return false;
        }

        // Redact at the provider first; only if that succeeds do we drop the local copy. If the provider
        // call fails it propagates, leaving both copies intact.
        if (notification.ProviderMessageSid != null && !notification.ContentDisposed)
        {
            await _smsProvider.RedactAsync(notification.ProviderMessageSid);
        }

        notification.MarkContentDisposed();
        await _notificationRepository.UpdateAsync(notification);
        _logger.LogInformation("Disposed of content for notification {NotificationId}.", notificationId);
        return true;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to)
    {
        var providerMessages = await _smsProvider.ListSentFromConfiguredSenderAsync(from, to);
        var providerSids = new HashSet<string>(providerMessages.Select(m => m.Sid), StringComparer.Ordinal);

        var allNotifications = await _notificationRepository.ListAsync();
        var eShopBySid = allNotifications
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();
        foreach (var m in providerMessages)
        {
            if (eShopBySid.TryGetValue(m.Sid, out var n))
            {
                matched.Add(new ReconciliationEntry(m.Sid, m.Status, n.OrderId, n.Kind.ToString(), m.DateSent));
            }
            else
            {
                providerOnly.Add(new ReconciliationEntry(m.Sid, m.Status, null, null, m.DateSent));
            }
        }

        // Messages eShop believes it actually sent, in-window, that the provider's record does not show.
        var eShopOnly = allNotifications
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid)
                        && SentStatuses.Contains(n.Status)
                        && n.CreatedAt >= from && n.CreatedAt <= to
                        && !providerSids.Contains(n.ProviderMessageSid!))
            .Select(n => new ReconciliationEntry(n.ProviderMessageSid, n.Status, n.OrderId, n.Kind.ToString(), null))
            .ToList();

        _logger.LogInformation(
            "Reconciliation {From:o}..{To:o}: provider {ProviderCount}, matched {Matched}, provider-only {ProviderOnly}, eShop-only {EShopOnly}.",
            from, to, providerMessages.Count, matched.Count, providerOnly.Count, eShopOnly.Count);

        return new ReconciliationReport(from, to, matched, providerOnly, eShopOnly);
    }

    // ---- messaging helpers ---------------------------------------------------------------------

    private async Task NotifyOwnerAsync(Order order, NotificationKind kind, string ownerId, DateTimeOffset? scheduledSendAt = null)
    {
        var numbers = await _contactRepository.ListAsync(new ContactNumbersByOwnerSpecification(ownerId));
        if (numbers.Count == 0)
        {
            // A shopper with no number on file is simply not messaged.
            _logger.LogInformation("No contact number on file for the owner of order {OrderId}; skipping {Kind} message.", order.Id, kind);
            return;
        }

        var body = BuildBody(kind, order.Id);
        foreach (var number in numbers)
        {
            var notification = new OrderNotification(order.Id, ownerId, kind, number.PhoneNumber, body);
            try
            {
                var pm = scheduledSendAt.HasValue
                    ? await _smsProvider.ScheduleAsync(number.PhoneNumber, body, scheduledSendAt.Value)
                    : await _smsProvider.SendAsync(number.PhoneNumber, body);
                notification.RecordAccepted(pm.Sid, pm.Status ?? string.Empty, scheduledSendAt);
            }
            catch (SmsProviderException ex)
            {
                // A message that cannot be sent must never fail the underlying operation.
                notification.RecordNotSent(null, ex.ProviderErrorCode);
                _logger.LogWarning("{Kind} message for order {OrderId} was not accepted by the provider (code {Code}).", kind, order.Id, ex.ProviderErrorCode);
            }
            catch (Exception)
            {
                notification.RecordNotSent(null);
                _logger.LogWarning("{Kind} message for order {OrderId} could not be sent.", kind, order.Id);
            }

            await _notificationRepository.AddAsync(notification);
        }
    }

    private async Task RefreshAsync(IEnumerable<OrderNotification> notifications)
    {
        foreach (var n in notifications)
        {
            if (n.ProviderMessageSid == null || TerminalStatuses.Contains(n.Status))
            {
                continue;
            }
            await TryRefreshAsync(n);
        }
    }

    private async Task TryRefreshAsync(OrderNotification n)
    {
        if (n.ProviderMessageSid == null)
        {
            return;
        }
        try
        {
            var pm = await _smsProvider.FetchAsync(n.ProviderMessageSid);
            n.UpdateDeliveryState(pm.Status, pm.ErrorCode, pm.ErrorMessage);
            await _notificationRepository.UpdateAsync(n);
        }
        catch (SmsProviderException ex)
        {
            _logger.LogWarning("Could not refresh delivery state for notification {NotificationId} (provider code {Code}).", n.Id, ex.ProviderErrorCode);
        }
    }

    private static string BuildBody(NotificationKind kind, int orderId) => kind switch
    {
        NotificationKind.OrderPlaced => $"eShopOnWeb: Thanks! Your order #{orderId} has been placed.",
        NotificationKind.OrderDispatched => $"eShopOnWeb: Good news - your order #{orderId} is on its way!",
        NotificationKind.DeliveryFollowUp => $"eShopOnWeb: How did the delivery of your order #{orderId} go? We'd love your feedback.",
        NotificationKind.OrderCancelled => $"eShopOnWeb: Your order #{orderId} has been cancelled. If this is unexpected, please contact support.",
        _ => $"eShopOnWeb: An update on your order #{orderId}."
    };
}
