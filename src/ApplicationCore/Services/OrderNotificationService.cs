using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    public static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly ISmsGateway _smsGateway;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IRepository<ContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        ISmsGateway smsGateway,
        IUriComposer uriComposer,
        IAppLogger<OrderNotificationService> logger)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _smsGateway = smsGateway;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<OrderLine> lines,
        ShipToAddress? shipTo,
        CancellationToken ct)
    {
        if (lines is null || lines.Count == 0)
        {
            throw new ArgumentException("At least one catalog item is required.");
        }

        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new ArgumentException("Each item quantity must be greater than zero.");
            }
        }

        var catalogIds = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(catalogIds), ct);
        if (catalogItems.Count != catalogIds.Length)
        {
            throw new ArgumentException("One or more catalog items were not found.");
        }

        var address = shipTo is null
            ? new Address("123 Maple Street", "Seattle", "WA", "United States", "98101")
            : new Address(shipTo.Street, shipTo.City, shipTo.State, shipTo.Country, shipTo.ZipCode);

        var orderItems = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, address, orderItems);
        await _orders.AddAsync(order, ct);

        await TryNotifyAsync(
            order,
            NotificationKind.OrderPlaced,
            $"Your eShopOnWeb order #{order.Id} has been placed. Thank you for your purchase.",
            scheduledSendAt: null,
            ct);

        return order;
    }

    public async Task DispatchAsync(int orderId, CancellationToken ct)
    {
        var order = await GetOrderOrThrow(orderId, ct);
        order.MarkDispatched();
        await _orders.UpdateAsync(order, ct);

        await TryNotifyAsync(
            order,
            NotificationKind.OrderDispatched,
            $"Your eShopOnWeb order #{order.Id} is on its way.",
            scheduledSendAt: null,
            ct);

        await TryNotifyAsync(
            order,
            NotificationKind.DeliveryFollowUp,
            $"How did the delivery of your eShopOnWeb order #{order.Id} go?",
            scheduledSendAt: DateTimeOffset.UtcNow.Add(FollowUpDelay),
            ct);
    }

    public async Task CancelAsync(int orderId, CancellationToken ct)
    {
        var order = await GetOrderOrThrow(orderId, ct);
        order.MarkCancelled();
        await _orders.UpdateAsync(order, ct);

        var followUps = await _notifications.ListAsync(new ScheduledFollowUpsByOrderSpecification(orderId), ct);
        foreach (var followUp in followUps)
        {
            if (string.IsNullOrEmpty(followUp.ProviderMessageSid))
            {
                continue;
            }

            if (!followUp.IsScheduledFollowUp()
                && !string.Equals(followUp.ProviderStatus, "queued", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(followUp.ProviderStatus, "accepted", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(followUp.ProviderStatus, "pending", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                var snapshot = await _smsGateway.CancelScheduledAsync(followUp.ProviderMessageSid, ct);
                followUp.SyncFromProvider(snapshot.Status, snapshot.ErrorCode, snapshot.ErrorMessage, snapshot.Body);
                await _notifications.UpdateAsync(followUp, ct);
            }
            catch (SmsGatewayException)
            {
                _logger.LogWarning(
                    "Failed to cancel scheduled follow-up {NotificationId} for order {OrderId}.",
                    followUp.Id,
                    orderId);
            }
        }

        await TryNotifyAsync(
            order,
            NotificationKind.OrderCancelled,
            $"Your eShopOnWeb order #{order.Id} has been cancelled.",
            scheduledSendAt: null,
            ct);
    }

    public async Task<BuyerOrdersResult> GetMyOrdersAsync(string buyerId, CancellationToken ct)
    {
        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);
        var notifications = await _notifications.ListAsync(new OrderNotificationsByBuyerSpecification(buyerId), ct);
        await RefreshAsync(notifications, ct);
        return new BuyerOrdersResult(orders, notifications);
    }

    public async Task<IReadOnlyList<OrderNotification>> ListOrderNotificationsAsync(
        int orderId,
        string? buyerId,
        CancellationToken ct)
    {
        var order = await GetOrderOrThrow(orderId, ct);
        if (buyerId is not null && !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new KeyNotFoundException("Order not found.");
        }

        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderSpecification(orderId), ct);
        await RefreshAsync(notifications, ct);
        return notifications;
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("An idempotency key is required.");
        }

        var original = await _notifications.GetByIdAsync(notificationId, ct);
        if (original is null)
        {
            throw new KeyNotFoundException("Notification not found.");
        }

        var existing = await _notifications.FirstOrDefaultAsync(
            new OrderNotificationByResendKeySpecification(original.Id, idempotencyKey), ct);
        if (existing is not null)
        {
            return existing;
        }

        var body = original.Body;
        if (original.ContentRedacted || string.IsNullOrWhiteSpace(body))
        {
            throw new InvalidOperationException("The message content is no longer available to resend.");
        }

        var destination = await ResolveDestinationAsync(original, ct);
        var resend = new OrderNotification(
            original.OrderId,
            original.BuyerId,
            NotificationKind.Resend,
            body,
            destination?.Id ?? original.ContactNumberId,
            scheduledSendAt: null,
            resendOfNotificationId: original.Id,
            idempotencyKey: idempotencyKey);

        if (destination is null)
        {
            resend.RecordProviderFailure("skipped", null, "No contact number on file.");
            return await _notifications.AddAsync(resend, ct);
        }

        try
        {
            var snapshot = await _smsGateway.SendAsync(destination.CanonicalNumber, body, ct);
            resend.RecordProviderAccepted(snapshot.Sid, snapshot.Status, snapshot.ErrorCode, snapshot.ErrorMessage);
        }
        catch (SmsGatewayException ex)
        {
            _logger.LogWarning(
                "Resend of notification {NotificationId} failed with HTTP {Status}.",
                original.Id,
                ex.StatusCode?.ToString() ?? "none");
            resend.RecordProviderFailure("failed", (int?)ex.StatusCode, ex.Message);
        }

        return await _notifications.AddAsync(resend, ct);
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken ct)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, ct);
        if (notification is null)
        {
            throw new KeyNotFoundException("Notification not found.");
        }

        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            try
            {
                var snapshot = await _smsGateway.RedactBodyAsync(notification.ProviderMessageSid, ct);
                notification.SyncFromProvider(snapshot.Status, snapshot.ErrorCode, snapshot.ErrorMessage, snapshot.Body);
            }
            catch (SmsGatewayException ex)
            {
                throw new InvalidOperationException(
                    "The provider could not dispose of the message content.", ex);
            }
        }

        notification.MarkContentRedacted();
        await _notifications.UpdateAsync(notification, ct);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var providerList = await _smsGateway.ListFromConfiguredNumberAsync(from, to, ct);
        var providerMessages = providerList.Messages;
        var local = await _notifications.ListAsync(new OrderNotificationsWithProviderSidSpecification(), ct);

        var localBySid = local
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var providerSids = new HashSet<string>(StringComparer.Ordinal);
        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();

        foreach (var message in providerMessages)
        {
            if (string.IsNullOrEmpty(message.Sid))
            {
                continue;
            }

            providerSids.Add(message.Sid);
            if (localBySid.TryGetValue(message.Sid, out var notification))
            {
                matched.Add(new ReconciliationEntry(
                    notification.Id,
                    message.Sid,
                    message.Status,
                    message.DateSent,
                    notification.Kind.ToString()));
            }
            else
            {
                providerOnly.Add(new ReconciliationEntry(
                    null,
                    message.Sid,
                    message.Status,
                    message.DateSent,
                    null));
            }
        }

        var localOnly = local
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid) && !providerSids.Contains(n.ProviderMessageSid!))
            .Where(n => n.CreatedAt >= from && n.CreatedAt <= to)
            .Select(n => new ReconciliationEntry(n.Id, n.ProviderMessageSid, n.ProviderStatus, null, n.Kind.ToString()))
            .ToList();

        return new ReconciliationReport(
            from,
            to,
            providerList.FromNumber,
            matched,
            providerOnly,
            localOnly,
            providerList.Truncated);
    }

    private async Task TryNotifyAsync(
        Order order,
        NotificationKind kind,
        string body,
        DateTimeOffset? scheduledSendAt,
        CancellationToken ct)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(order.BuyerId), ct);
        if (numbers.Count == 0)
        {
            _logger.LogInformation(
                "Skipping {Kind} notification for order {OrderId}; no contact number on file.",
                kind,
                order.Id);
            return;
        }

        foreach (var number in numbers)
        {
            var notification = new OrderNotification(
                order.Id,
                order.BuyerId,
                kind,
                body,
                number.Id,
                scheduledSendAt);

            try
            {
                SmsMessageSnapshot snapshot;
                if (scheduledSendAt is null)
                {
                    snapshot = await _smsGateway.SendAsync(number.CanonicalNumber, body, ct);
                }
                else
                {
                    snapshot = await _smsGateway.ScheduleAsync(number.CanonicalNumber, body, scheduledSendAt.Value, ct);
                }

                notification.RecordProviderAccepted(snapshot.Sid, snapshot.Status, snapshot.ErrorCode, snapshot.ErrorMessage);
            }
            catch (SmsGatewayException ex)
            {
                _logger.LogWarning(
                    "Failed to send {Kind} notification for order {OrderId} with HTTP {Status}.",
                    kind,
                    order.Id,
                    ex.StatusCode?.ToString() ?? "none");
                notification.RecordProviderFailure("failed", (int?)ex.StatusCode, ex.Message);
            }

            await _notifications.AddAsync(notification, ct);
        }
    }

    private async Task RefreshAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken ct)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var snapshot = await _smsGateway.FetchAsync(notification.ProviderMessageSid, ct);
                notification.SyncFromProvider(snapshot.Status, snapshot.ErrorCode, snapshot.ErrorMessage, snapshot.Body);
                if (notification.ContentRedacted)
                {
                    notification.MarkContentRedacted();
                }
                await _notifications.UpdateAsync(notification, ct);
            }
            catch (SmsGatewayException)
            {
                _logger.LogWarning(
                    "Failed to refresh provider status for notification {NotificationId}.",
                    notification.Id);
            }
        }
    }

    private async Task<Order> GetOrderOrThrow(int orderId, CancellationToken ct)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderByIdSpecification(orderId), ct);
        if (order is null)
        {
            throw new KeyNotFoundException("Order not found.");
        }

        return order;
    }

    private async Task<ContactNumber?> ResolveDestinationAsync(OrderNotification original, CancellationToken ct)
    {
        if (original.ContactNumberId is int contactNumberId)
        {
            var byId = await _contactNumbers.GetByIdAsync(contactNumberId, ct);
            if (byId is not null && string.Equals(byId.BuyerId, original.BuyerId, StringComparison.Ordinal))
            {
                return byId;
            }
        }

        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(original.BuyerId), ct);
        return numbers.FirstOrDefault();
    }
}
