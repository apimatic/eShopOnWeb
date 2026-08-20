using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Extensions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ShopOrderService : IShopOrderService
{
    public static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private static readonly Address DefaultShipToAddress =
        new("123 Main St.", "Kent", "OH", "United States", "44240");

    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<NotificationResendIdempotency> _resendKeys;
    private readonly ISmsMessageGateway _sms;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<ShopOrderService> _logger;

    public ShopOrderService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IRepository<ContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        IRepository<NotificationResendIdempotency> resendKeys,
        ISmsMessageGateway sms,
        IUriComposer uriComposer,
        IAppLogger<ShopOrderService> logger)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _resendKeys = resendKeys;
        _sms = sms;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<CatalogOrderLine> items, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (items == null || items.Count == 0)
        {
            throw new ArgumentException("An order must contain at least one item.", nameof(items));
        }

        var ids = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        var byId = catalogItems.ToDictionary(c => c.Id);

        var orderItems = new List<OrderItem>();
        foreach (var line in items)
        {
            if (line.Quantity <= 0)
            {
                throw new ArgumentException("Quantity must be greater than zero.");
            }

            if (!byId.TryGetValue(line.CatalogItemId, out var catalogItem))
            {
                throw new ArgumentException($"Catalog item {line.CatalogItemId} was not found.");
            }

            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, DefaultShipToAddress, orderItems);
        await _orders.AddAsync(order, cancellationToken);

        await TryNotifyAsync(
            order,
            NotificationKind.OrderPlaced,
            $"Your eShopOnWeb order #{order.Id} has been placed.",
            sendAt: null,
            cancellationToken);

        return order;
    }

    public async Task<Order> DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetRequiredOrderAsync(orderId, cancellationToken);
        var alreadyDispatched = order.Status == OrderStatus.Dispatched;
        order.MarkDispatched();
        await _orders.UpdateAsync(order, cancellationToken);

        if (alreadyDispatched)
        {
            return order;
        }

        await TryNotifyAsync(
            order,
            NotificationKind.OrderDispatched,
            $"Your eShopOnWeb order #{order.Id} is on its way.",
            sendAt: null,
            cancellationToken);

        await TryNotifyAsync(
            order,
            NotificationKind.DeliveryFollowUp,
            $"How did the delivery of your eShopOnWeb order #{order.Id} go?",
            sendAt: DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay),
            cancellationToken);

        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetRequiredOrderAsync(orderId, cancellationToken);
        var alreadyCancelled = order.Status == OrderStatus.Cancelled;
        order.MarkCancelled();
        await _orders.UpdateAsync(order, cancellationToken);

        await CancelOutstandingFollowUpsAsync(order.Id, cancellationToken);

        if (!alreadyCancelled)
        {
            await TryNotifyAsync(
                order,
                NotificationKind.OrderCancelled,
                $"Your eShopOnWeb order #{order.Id} has been cancelled.",
                sendAt: null,
                cancellationToken);
        }

        return order;
    }

    public async Task<IReadOnlyList<Order>> ListBuyerOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        return orders;
    }

    public async Task<Order?> GetOrderForCallerAsync(int orderId, string buyerId, bool isAdministrator, CancellationToken cancellationToken = default)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order == null)
        {
            return null;
        }

        if (!isAdministrator && order.BuyerId != buyerId)
        {
            return null;
        }

        return order;
    }

    public async Task<IReadOnlyList<OrderNotification>> ListNotificationsForOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var notifications = await _notifications.ListAsync(new NotificationsByOrderIdSpec(orderId), cancellationToken);
        await RefreshFromProviderAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<IReadOnlyList<OrderNotification>> ListNotificationsForBuyerOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orders.ListAsync(new CustomerOrdersSpecification(buyerId), cancellationToken);
        if (orders.Count == 0)
        {
            return Array.Empty<OrderNotification>();
        }

        var notifications = await _notifications.ListAsync(
            new NotificationsByOrderIdsSpec(orders.Select(o => o.Id)), cancellationToken);
        await RefreshFromProviderAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var existing = await _resendKeys.FirstOrDefaultAsync(
            new ResendIdempotencySpec(notificationId, idempotencyKey), cancellationToken);
        if (existing != null)
        {
            var previous = await _notifications.GetByIdAsync(existing.ResultNotificationId, cancellationToken);
            if (previous != null)
            {
                await RefreshFromProviderAsync(new[] { previous }, cancellationToken);
                return previous;
            }
        }

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new KeyNotFoundException($"Notification {notificationId} was not found.");

        await RefreshFromProviderAsync(new[] { original }, cancellationToken);

        if (!original.DidNotReachShopper())
        {
            throw new OrderStateException("Only messages that did not reach the shopper can be re-sent.");
        }

        var destination = await ResolveResendDestinationAsync(original, cancellationToken);
        var body = original.BodyRedacted || string.IsNullOrEmpty(original.Body)
            ? ReconstructBody(original)
            : original.Body;

        var resend = new OrderNotification(original.OrderId, original.BuyerId, NotificationKind.Resend, body, destination);
        resend.RecordOriginalNotification(original.Id);
        await _notifications.AddAsync(resend, cancellationToken);

        if (destination != null)
        {
            await DispatchToProviderAsync(resend, body, sendAt: null, cancellationToken);
        }
        else
        {
            _logger.LogInformation("Skipped resend of notification {NotificationId}: no reachable number on file.", notificationId);
        }

        await _resendKeys.AddAsync(
            new NotificationResendIdempotency(original.Id, idempotencyKey, resend.Id),
            cancellationToken);

        return resend;
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new KeyNotFoundException($"Notification {notificationId} was not found.");

        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            try
            {
                await _sms.RedactBodyAsync(notification.ProviderMessageSid, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Failed to redact provider content for notification {NotificationId}: {Message}",
                    notificationId,
                    LogSanitizer.RedactPhoneNumbers(ex.Message));
                throw;
            }
        }

        notification.MarkContentRedacted();
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new ArgumentException("The 'to' timestamp must be on or after 'from'.");
        }

        var providerMessages = await _sms.ListSentFromConfiguredNumberAsync(from, to, cancellationToken);
        var local = await _notifications.ListAsync(new NotificationsInCreatedRangeSpec(from, to), cancellationToken);

        var localBySid = local
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var providerBySid = providerMessages
            .GroupBy(m => m.Sid, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();
        var eshopOnly = new List<ReconciliationEntry>();

        foreach (var provider in providerMessages)
        {
            if (localBySid.TryGetValue(provider.Sid, out var notification))
            {
                matched.Add(ToEntry(notification, provider));
            }
            else
            {
                providerOnly.Add(new ReconciliationEntry
                {
                    ProviderMessageSid = provider.Sid,
                    ProviderStatus = provider.Status,
                    DateSent = provider.DateSent
                });
            }
        }

        foreach (var notification in local)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid) ||
                !providerBySid.ContainsKey(notification.ProviderMessageSid))
            {
                eshopOnly.Add(ToEntry(notification, provider: null));
            }
        }

        return new ReconciliationReport
        {
            From = from,
            To = to,
            FromNumber = _sms.ConfiguredFromNumber,
            Matched = matched,
            ProviderOnly = providerOnly,
            EshopOnly = eshopOnly
        };
    }

    private static ReconciliationEntry ToEntry(OrderNotification notification, SmsMessageSnapshot? provider)
    {
        return new ReconciliationEntry
        {
            NotificationId = notification.Id,
            ProviderMessageSid = notification.ProviderMessageSid ?? provider?.Sid,
            EshopStatus = notification.ProviderStatus,
            ProviderStatus = provider?.Status,
            DateSent = provider?.DateSent ?? notification.ProviderDateSent
        };
    }

    private async Task<Order> GetRequiredOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order == null)
        {
            throw new KeyNotFoundException($"Order {orderId} was not found.");
        }

        return order;
    }

    private async Task TryNotifyAsync(
        Order order,
        NotificationKind kind,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var destination = await GetActiveDestinationAsync(order.BuyerId, cancellationToken);
            var notification = new OrderNotification(order.Id, order.BuyerId, kind, body, destination);
            if (sendAt.HasValue)
            {
                notification.RecordScheduledSendAt(sendAt.Value);
            }

            await _notifications.AddAsync(notification, cancellationToken);

            if (destination == null)
            {
                _logger.LogInformation(
                    "Skipped {Kind} notification {NotificationId} for order {OrderId}: no contact number on file.",
                    kind, notification.Id, order.Id);
                return;
            }

            await DispatchToProviderAsync(notification, body, sendAt, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Failed {Kind} notification for order {OrderId}: {Message}",
                kind,
                order.Id,
                LogSanitizer.RedactPhoneNumbers(ex.Message));
        }
    }

    private async Task DispatchToProviderAsync(
        OrderNotification notification,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(notification.DestinationPhoneNumber))
        {
            return;
        }

        try
        {
            var result = await _sms.SendAsync(notification.DestinationPhoneNumber, body, sendAt, cancellationToken);
            if (result.Accepted && !string.IsNullOrEmpty(result.ProviderMessageSid))
            {
                notification.RecordProviderAcceptance(result.ProviderMessageSid, result.Status);
            }
            else
            {
                notification.RecordSendFailure(result.ErrorCode);
            }

            await _notifications.UpdateAsync(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            notification.RecordSendFailure(null);
            await _notifications.UpdateAsync(notification, cancellationToken);
            _logger.LogWarning(
                "Provider rejected send for notification {NotificationId}: {Message}",
                notification.Id,
                LogSanitizer.RedactPhoneNumbers(ex.Message));
        }
    }

    private async Task CancelOutstandingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var followUps = await _notifications.ListAsync(new ScheduledFollowUpsByOrderIdSpec(orderId), cancellationToken);
        foreach (var followUp in followUps)
        {
            if (string.IsNullOrEmpty(followUp.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var snapshot = await _sms.FetchAsync(followUp.ProviderMessageSid, cancellationToken);
                if (snapshot != null && !string.Equals(snapshot.Status, "scheduled", StringComparison.OrdinalIgnoreCase))
                {
                    followUp.SyncProviderState(snapshot.Status, snapshot.Body, snapshot.ErrorCode, snapshot.DateSent);
                    await _notifications.UpdateAsync(followUp, cancellationToken);
                    continue;
                }

                await _sms.CancelScheduledAsync(followUp.ProviderMessageSid, cancellationToken);
                var cancelled = await _sms.FetchAsync(followUp.ProviderMessageSid, cancellationToken);
                if (cancelled != null)
                {
                    followUp.SyncProviderState(cancelled.Status, cancelled.Body, cancelled.ErrorCode, cancelled.DateSent);
                }
                else
                {
                    followUp.SyncProviderState("canceled", followUp.Body, null, null);
                }

                await _notifications.UpdateAsync(followUp, cancellationToken);
                _logger.LogInformation(
                    "Cancelled scheduled follow-up notification {NotificationId} for order {OrderId}.",
                    followUp.Id, orderId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Failed to cancel scheduled follow-up {NotificationId} for order {OrderId}: {Message}",
                    followUp.Id,
                    orderId,
                    LogSanitizer.RedactPhoneNumbers(ex.Message));
            }
        }
    }

    private async Task RefreshFromProviderAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var snapshot = await _sms.FetchAsync(notification.ProviderMessageSid, cancellationToken);
                if (snapshot == null)
                {
                    continue;
                }

                notification.SyncProviderState(snapshot.Status, snapshot.Body, snapshot.ErrorCode, snapshot.DateSent);
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Failed to refresh provider status for notification {NotificationId}: {Message}",
                    notification.Id,
                    LogSanitizer.RedactPhoneNumbers(ex.Message));
            }
        }
    }

    private async Task<string?> GetActiveDestinationAsync(string buyerId, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerIdSpec(buyerId), cancellationToken);
        return numbers.FirstOrDefault()?.PhoneNumber;
    }

    private async Task<string?> ResolveResendDestinationAsync(OrderNotification original, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerIdSpec(original.BuyerId), cancellationToken);
        if (numbers.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrEmpty(original.DestinationPhoneNumber) &&
            numbers.Any(n => n.PhoneNumber == original.DestinationPhoneNumber))
        {
            return original.DestinationPhoneNumber;
        }

        return numbers[0].PhoneNumber;
    }

    private static string ReconstructBody(OrderNotification original)
    {
        return original.Kind switch
        {
            NotificationKind.OrderPlaced => $"Your eShopOnWeb order #{original.OrderId} has been placed.",
            NotificationKind.OrderDispatched => $"Your eShopOnWeb order #{original.OrderId} is on its way.",
            NotificationKind.DeliveryFollowUp => $"How did the delivery of your eShopOnWeb order #{original.OrderId} go?",
            NotificationKind.OrderCancelled => $"Your eShopOnWeb order #{original.OrderId} has been cancelled.",
            _ => $"Update for your eShopOnWeb order #{original.OrderId}."
        };
    }
}
