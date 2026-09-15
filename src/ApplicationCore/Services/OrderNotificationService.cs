using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    public static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IContactNumberService _contactNumbers;
    private readonly ISmsGateway _sms;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<OrderNotification> notificationRepository,
        IContactNumberService contactNumbers,
        ISmsGateway sms,
        IUriComposer uriComposer,
        IAppLogger<OrderNotificationService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _notificationRepository = notificationRepository;
        _contactNumbers = contactNumbers;
        _sms = sms;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<CatalogOrderLine> lines,
        Address? shipTo,
        CancellationToken cancellationToken)
    {
        if (lines is null || lines.Count == 0)
        {
            throw new ArgumentException("At least one catalog item is required.", nameof(lines));
        }

        if (lines.Any(l => l.Quantity <= 0))
        {
            throw new ArgumentException("Each item quantity must be greater than zero.", nameof(lines));
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        if (catalogItems.Count != ids.Length)
        {
            throw new ArgumentException("One or more catalog items were not found.", nameof(lines));
        }

        var address = shipTo ?? new Address("123 Main St.", "Kent", "OH", "United States", "44240");
        var items = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, address, items);
        await _orderRepository.AddAsync(order, cancellationToken);

        await TryNotifyAsync(
            order,
            NotificationKind.OrderPlaced,
            $"Your eShopOnWeb order #{order.Id} has been placed.",
            cancellationToken);

        return order;
    }

    public async Task<Order> DispatchAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken)
                    ?? throw new KeyNotFoundException($"Order {orderId} was not found.");

        order.MarkDispatched();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        var dispatched = await TryNotifyAsync(
            order,
            NotificationKind.OrderDispatched,
            $"Your eShopOnWeb order #{order.Id} is on its way.",
            cancellationToken);

        await TryScheduleFollowUpAsync(order, dispatched, cancellationToken);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken)
                    ?? throw new KeyNotFoundException($"Order {orderId} was not found.");

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        await CancelPendingFollowUpsAsync(order.Id, cancellationToken);

        await TryNotifyAsync(
            order,
            NotificationKind.OrderCancelled,
            $"Your eShopOnWeb order #{order.Id} has been cancelled.",
            cancellationToken);

        return order;
    }

    public async Task<IReadOnlyList<Order>> ListBuyerOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        return await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>> ListNotificationsForOrderAsync(
        int orderId,
        string buyerId,
        CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null || !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new KeyNotFoundException($"Order {orderId} was not found.");
        }

        var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderSpec(orderId), cancellationToken);
        await RefreshFromProviderAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<IReadOnlyList<OrderNotification>> ListNotificationsForOrdersAsync(
        IReadOnlyList<int> orderIds,
        CancellationToken cancellationToken)
    {
        if (orderIds.Count == 0)
        {
            return Array.Empty<OrderNotification>();
        }

        var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderIdsSpec(orderIds), cancellationToken);
        await RefreshFromProviderAsync(notifications.Take(20).ToList(), cancellationToken);
        return notifications;
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("An idempotency key is required.", nameof(idempotencyKey));
        }

        var original = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken)
                       ?? throw new KeyNotFoundException($"Notification {notificationId} was not found.");

        var existing = await _notificationRepository.FirstOrDefaultAsync(
            new NotificationByIdempotencySpec(original.Id, idempotencyKey), cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var destination = original.Destination
                          ?? await _contactNumbers.GetPreferredCanonicalNumberAsync(original.BuyerId, cancellationToken);
        var body = original.ContentRedacted
            ? $"Your eShopOnWeb order #{original.OrderId} has an update."
            : (original.Body ?? $"Your eShopOnWeb order #{original.OrderId} has an update.");

        var resend = new OrderNotification(original.OrderId, original.BuyerId, NotificationKind.Resend, destination, body);
        resend.MarkResendOf(original.Id, idempotencyKey);

        if (string.IsNullOrEmpty(destination))
        {
            resend.ApplyProviderResult(null, "skipped", null, "No destination on file.", null);
            await _notificationRepository.AddAsync(resend, cancellationToken);
            return resend;
        }

        var result = await _sms.SendAsync(destination, body, cancellationToken);
        ApplyResult(resend, result);
        await _notificationRepository.AddAsync(resend, cancellationToken);
        _logger.LogInformation("Resent notification {SourceNotificationId} as {NotificationId} for order {OrderId}.",
            original.Id, resend.Id, original.OrderId);
        return resend;
    }

    public async Task<OrderNotification> DisposeContentAsync(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken)
                           ?? throw new KeyNotFoundException($"Notification {notificationId} was not found.");

        if (!string.IsNullOrEmpty(notification.ProviderSid))
        {
            var result = await _sms.RedactBodyAsync(notification.ProviderSid, cancellationToken);
            if (!result.OutcomeUnknown && result.ProviderSid is null && result.ErrorMessage is not null
                && result.Status == "failed")
            {
                _logger.LogWarning("Provider content disposal failed for notification {NotificationId}.", notificationId);
            }
        }

        notification.RedactContent();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
        return notification;
    }

    public async Task<NotificationReconciliationReport> ReconcileAsync(
        DateTimeOffset rangeStart,
        DateTimeOffset rangeEnd,
        CancellationToken cancellationToken)
    {
        if (rangeEnd < rangeStart)
        {
            throw new ArgumentException("The end of the range must be on or after the start.");
        }

        var providerMessages = await _sms.ListSentFromConfiguredNumberAsync(rangeStart, rangeEnd, cancellationToken);
        var truncated = providerMessages.Count >= 50 * 1000;

        var local = await _notificationRepository.ListAsync(
            new NotificationsCreatedInRangeSpec(rangeStart.AddHours(-1), rangeEnd.AddHours(1)), cancellationToken);

        var localBySid = local
            .Where(n => !string.IsNullOrEmpty(n.ProviderSid))
            .GroupBy(n => n.ProviderSid!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var providerSids = new HashSet<string>(
            providerMessages.Where(m => !string.IsNullOrEmpty(m.Sid)).Select(m => m.Sid),
            StringComparer.OrdinalIgnoreCase);

        var matched = new List<ReconciliationRow>();
        var providerOnly = new List<ReconciliationRow>();
        var localOnly = new List<ReconciliationRow>();

        foreach (var message in providerMessages)
        {
            if (localBySid.TryGetValue(message.Sid, out var notification))
            {
                matched.Add(ToRow(notification, message));
            }
            else
            {
                providerOnly.Add(new ReconciliationRow(message.Sid, null, message.Status, message.DateSentRaw, null));
            }
        }

        foreach (var notification in local)
        {
            if (string.IsNullOrEmpty(notification.ProviderSid))
            {
                localOnly.Add(ToRow(notification, null));
                continue;
            }

            if (!providerSids.Contains(notification.ProviderSid))
            {
                localOnly.Add(ToRow(notification, null));
            }
        }

        return new NotificationReconciliationReport(
            rangeStart,
            rangeEnd,
            _sms.SendingNumber,
            matched,
            providerOnly,
            localOnly,
            truncated);
    }

    private async Task<OrderNotification?> TryNotifyAsync(
        Order order,
        NotificationKind kind,
        string body,
        CancellationToken cancellationToken)
    {
        string? destination = null;
        try
        {
            destination = await _contactNumbers.GetPreferredCanonicalNumberAsync(order.BuyerId, cancellationToken);
        }
        catch (Exception)
        {
            _logger.LogWarning("Could not load a destination for order {OrderId}; the order still succeeded.", order.Id);
        }

        if (string.IsNullOrEmpty(destination))
        {
            _logger.LogInformation("No contact number on file for order {OrderId}; skipping {Kind}.", order.Id, kind);
            return null;
        }

        var notification = new OrderNotification(order.Id, order.BuyerId, kind, destination, body);
        SmsSendResult result;
        try
        {
            result = await _sms.SendAsync(destination, body, cancellationToken);
        }
        catch (Exception)
        {
            result = SmsSendResult.Unknown("The send could not be completed.");
            _logger.LogWarning("SMS send threw for order {OrderId} kind {Kind}; the order still succeeded.", order.Id, kind);
        }

        ApplyResult(notification, result);
        await _notificationRepository.AddAsync(notification, cancellationToken);
        return notification;
    }

    private async Task TryScheduleFollowUpAsync(Order order, OrderNotification? dispatched, CancellationToken cancellationToken)
    {
        string? destination = dispatched?.Destination;
        destination ??= await _contactNumbers.GetPreferredCanonicalNumberAsync(order.BuyerId, cancellationToken);
        if (string.IsNullOrEmpty(destination))
        {
            return;
        }

        var sendAt = DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay);
        var body = $"How did the delivery of eShopOnWeb order #{order.Id} go?";
        var notification = new OrderNotification(order.Id, order.BuyerId, NotificationKind.DeliveryFollowUp, destination, body);
        notification.MarkScheduled(sendAt);
        if (dispatched is not null)
        {
            notification.MarkFollowUpOf(dispatched.Id);
        }

        SmsSendResult result;
        try
        {
            result = await _sms.ScheduleAsync(destination, body, sendAt, cancellationToken);
        }
        catch (Exception)
        {
            result = SmsSendResult.Unknown("The scheduled send could not be completed.");
            _logger.LogWarning("Scheduling a follow-up threw for order {OrderId}; dispatch still succeeded.", order.Id);
        }

        ApplyResult(notification, result);
        await _notificationRepository.AddAsync(notification, cancellationToken);
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var followUps = await _notificationRepository.ListAsync(new FollowUpsPendingForOrderSpec(orderId), cancellationToken);
        foreach (var followUp in followUps)
        {
            if (string.IsNullOrEmpty(followUp.ProviderSid))
            {
                continue;
            }

            SmsSendResult result;
            try
            {
                result = await _sms.CancelScheduledAsync(followUp.ProviderSid, cancellationToken);
            }
            catch (Exception)
            {
                result = SmsSendResult.Unknown("The scheduled follow-up could not be cancelled.");
                _logger.LogWarning("Cancelling follow-up {NotificationId} threw for order {OrderId}.", followUp.Id, orderId);
            }

            ApplyResult(followUp, result);
            await _notificationRepository.UpdateAsync(followUp, cancellationToken);
        }
    }

    private async Task RefreshFromProviderAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderSid))
            {
                continue;
            }

            try
            {
                var result = await _sms.FetchAsync(notification.ProviderSid, cancellationToken);
                ApplyResult(notification, result);
                await _notificationRepository.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception)
            {
                _logger.LogWarning("Could not refresh notification {NotificationId} from the provider.", notification.Id);
            }
        }
    }

    private static void ApplyResult(OrderNotification notification, SmsSendResult result)
    {
        notification.ApplyProviderResult(
            result.ProviderSid,
            result.Status,
            result.ErrorCode,
            result.ErrorMessage,
            result.DateSent);
    }

    private static ReconciliationRow ToRow(OrderNotification notification, ProviderMessageRecord? message)
    {
        return new ReconciliationRow(
            notification.ProviderSid ?? message?.Sid,
            notification.Id,
            message?.Status ?? notification.Status,
            message?.DateSentRaw ?? notification.DateSent,
            notification.Kind.ToString());
    }
}
