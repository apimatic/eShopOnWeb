using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Extensions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ShopOrderService : IShopOrderService
{
    public static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<NotificationResend> _resends;
    private readonly IContactNumberService _contactNumbers;
    private readonly ISmsGateway _smsGateway;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<ShopOrderService> _logger;

    public ShopOrderService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IRepository<OrderNotification> notifications,
        IRepository<NotificationResend> resends,
        IContactNumberService contactNumbers,
        ISmsGateway smsGateway,
        IUriComposer uriComposer,
        IAppLogger<ShopOrderService> logger)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _notifications = notifications;
        _resends = resends;
        _contactNumbers = contactNumbers;
        _smsGateway = smsGateway;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(PlaceOrderCommand command, CancellationToken cancellationToken = default)
    {
        if (command.Items == null || command.Items.Count == 0)
        {
            throw new ClientRequestException("An order must contain at least one catalog item.");
        }

        if (command.Items.Any(i => i.Quantity <= 0))
        {
            throw new ClientRequestException("Each order item must have a quantity greater than zero.");
        }

        var ids = command.Items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        if (catalogItems.Count != ids.Length)
        {
            throw new ResourceNotFoundException("One or more catalog items were not found.");
        }

        var address = command.ShipToAddress ?? new Address("123 Main St.", "Kent", "OH", "United States", "44240");
        var orderItems = command.Items.Select(item =>
        {
            var catalogItem = catalogItems.First(c => c.Id == item.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, item.Quantity);
        }).ToList();

        var order = new Order(command.BuyerId, address, orderItems);
        order = await _orders.AddAsync(order, cancellationToken);

        await TryNotifyAsync(
            order,
            OrderNotificationKind.OrderPlaced,
            $"eShopOnWeb: Your order #{order.Id} has been placed. Thank you for your purchase.",
            sendAt: null,
            cancellationToken);

        return order;
    }

    public async Task<Order> DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetRequiredOrderAsync(orderId, cancellationToken);
        order.MarkDispatched();
        await _orders.UpdateAsync(order, cancellationToken);

        await TryNotifyAsync(
            order,
            OrderNotificationKind.OrderDispatched,
            $"eShopOnWeb: Your order #{order.Id} has been dispatched and is on its way.",
            sendAt: null,
            cancellationToken);

        await TryNotifyAsync(
            order,
            OrderNotificationKind.DeliveryFeedback,
            $"eShopOnWeb: How did the delivery of order #{order.Id} go? We would love to hear from you.",
            sendAt: DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay),
            cancellationToken);

        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetRequiredOrderAsync(orderId, cancellationToken);
        order.MarkCancelled();
        await _orders.UpdateAsync(order, cancellationToken);

        await CancelOutstandingFollowUpsAsync(order.Id, cancellationToken);

        await TryNotifyAsync(
            order,
            OrderNotificationKind.OrderCancelled,
            $"eShopOnWeb: Your order #{order.Id} has been cancelled.",
            sendAt: null,
            cancellationToken);

        return order;
    }

    public async Task<IReadOnlyList<Order>> ListOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
    }

    public async Task<Order> GetOrderForCallerAsync(int orderId, string buyerId, bool isAdministrator, CancellationToken cancellationToken = default)
    {
        var order = await GetRequiredOrderAsync(orderId, cancellationToken);
        EnsureOrderAccess(order, buyerId, isAdministrator);
        return order;
    }

    public async Task<IReadOnlyList<OrderNotification>> ListNotificationsAsync(int orderId, string buyerId, bool isAdministrator, CancellationToken cancellationToken = default)
    {
        var order = await GetRequiredOrderAsync(orderId, cancellationToken);
        EnsureOrderAccess(order, buyerId, isAdministrator);

        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderIdSpecification(orderId), cancellationToken);
        foreach (var notification in notifications)
        {
            await RefreshFromProviderAsync(notification, cancellationToken);
        }

        return notifications;
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ClientRequestException("An idempotency key is required.");
        }

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken)
                       ?? throw new ResourceNotFoundException("Notification not found.");

        var existing = await _resends.FirstOrDefaultAsync(
            new NotificationResendByKeySpecification(notificationId, idempotencyKey.Trim()),
            cancellationToken);
        if (existing != null)
        {
            var previous = await _notifications.GetByIdAsync(existing.ResultNotificationId, cancellationToken);
            if (previous != null)
            {
                await RefreshFromProviderAsync(previous, cancellationToken);
                return previous;
            }
        }

        var destination = await ResolveResendDestinationAsync(original, cancellationToken);
        if (destination == null)
        {
            throw new ClientRequestException("The shopper has no registered destination for a resend.");
        }

        var body = original.ContentRedacted || string.IsNullOrEmpty(original.Body)
            ? BuildFallbackBody(original)
            : original.Body;

        var resent = await TryNotifyAsync(
            original.OrderId,
            original.BuyerId,
            OrderNotificationKind.Resend,
            destination,
            body,
            sendAt: null,
            parentNotificationId: original.Id,
            cancellationToken);

        if (resent == null)
        {
            throw new ClientRequestException("The shopper has no registered destination for a resend.");
        }

        try
        {
            await _resends.AddAsync(new NotificationResend(original.Id, idempotencyKey.Trim(), resent.Id), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Idempotent resend record collision for notification {NotificationId}: {Message}",
                original.Id,
                PhoneNumberLogSanitizer.Redact(ex.Message));

            var raced = await _resends.FirstOrDefaultAsync(
                new NotificationResendByKeySpecification(notificationId, idempotencyKey.Trim()),
                cancellationToken);
            if (raced != null)
            {
                var previous = await _notifications.GetByIdAsync(raced.ResultNotificationId, cancellationToken);
                if (previous != null)
                {
                    return previous;
                }
            }
        }

        return resent;
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken)
                           ?? throw new ResourceNotFoundException("Notification not found.");

        if (!string.IsNullOrEmpty(notification.ProviderSid))
        {
            try
            {
                var snapshot = await _smsGateway.RedactBodyAsync(notification.ProviderSid, cancellationToken);
                if (snapshot != null)
                {
                    notification.ApplyProviderSnapshot(snapshot.Status, snapshot.ErrorCode, snapshot.Body, snapshot.DateCreated, snapshot.DateSent);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Failed to redact provider content for notification {NotificationId}: {Message}",
                    notification.Id,
                    PhoneNumberLogSanitizer.Redact(ex.Message));
                throw;
            }
        }

        notification.MarkRedacted();
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new ClientRequestException("The reconciliation 'to' value must be on or after 'from'.");
        }

        var fromNumber = _smsGateway.FromNumber;
        var providerMessages = await _smsGateway.ListSentFromAsync(fromNumber, from, to, cancellationToken);
        var applicationMessages = await _notifications.ListAsync(
            new OrderNotificationsByCreatedRangeSpecification(from, to),
            cancellationToken);

        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid))
            .GroupBy(m => m.Sid, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var applicationBySid = applicationMessages
            .Where(n => !string.IsNullOrEmpty(n.ProviderSid))
            .GroupBy(n => n.ProviderSid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var matched = new List<ReconciliationMatch>();
        var providerOnly = new List<ReconciliationProviderMessage>();
        var applicationOnly = new List<ReconciliationApplicationMessage>();

        foreach (var provider in providerMessages)
        {
            if (applicationBySid.TryGetValue(provider.Sid, out var local))
            {
                matched.Add(new ReconciliationMatch(local.Id, provider.Sid, provider.Status));
            }
            else
            {
                providerOnly.Add(new ReconciliationProviderMessage(provider.Sid, provider.Status, provider.DateCreated, provider.DateSent));
            }
        }

        foreach (var local in applicationMessages)
        {
            if (string.IsNullOrEmpty(local.ProviderSid) || !providerBySid.ContainsKey(local.ProviderSid))
            {
                applicationOnly.Add(new ReconciliationApplicationMessage(
                    local.Id,
                    local.ProviderSid,
                    local.ProviderStatus,
                    local.Kind.ToString()));
            }
        }

        return new ReconciliationReport(from, to, fromNumber, matched, providerOnly, applicationOnly);
    }

    private async Task<Order> GetRequiredOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var spec = new OrderWithItemsByIdSpec(orderId);
        var order = await _orders.FirstOrDefaultAsync(spec, cancellationToken);
        if (order == null)
        {
            throw new ResourceNotFoundException("Order not found.");
        }

        return order;
    }

    private static void EnsureOrderAccess(Order order, string buyerId, bool isAdministrator)
    {
        if (!isAdministrator && !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new ResourceNotFoundException("Order not found.");
        }
    }

    private async Task CancelOutstandingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var followUps = await _notifications.ListAsync(new ScheduledFollowUpsByOrderIdSpecification(orderId), cancellationToken);
        foreach (var notification in followUps)
        {
            if (string.IsNullOrEmpty(notification.ProviderSid))
            {
                continue;
            }

            try
            {
                var snapshot = await _smsGateway.FetchAsync(notification.ProviderSid, cancellationToken);
                if (snapshot != null && string.Equals(snapshot.Status, "scheduled", StringComparison.OrdinalIgnoreCase))
                {
                    snapshot = await _smsGateway.CancelScheduledAsync(notification.ProviderSid, cancellationToken) ?? snapshot;
                }

                if (snapshot != null)
                {
                    notification.ApplyProviderSnapshot(snapshot.Status, snapshot.ErrorCode, snapshot.Body, snapshot.DateCreated, snapshot.DateSent);
                    await _notifications.UpdateAsync(notification, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Failed to cancel follow-up notification {NotificationId} for order {OrderId}: {Message}",
                    notification.Id,
                    orderId,
                    PhoneNumberLogSanitizer.Redact(ex.Message));
            }
        }
    }

    private async Task TryNotifyAsync(
        Order order,
        OrderNotificationKind kind,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        var destination = await _contactNumbers.GetPrimaryForBuyerAsync(order.BuyerId, cancellationToken);
        if (destination == null)
        {
            return;
        }

        await TryNotifyAsync(order.Id, order.BuyerId, kind, destination.PhoneNumber, body, sendAt, parentNotificationId: null, cancellationToken);
    }

    private async Task<OrderNotification?> TryNotifyAsync(
        int orderId,
        string buyerId,
        OrderNotificationKind kind,
        string destinationPhoneNumber,
        string body,
        DateTimeOffset? sendAt,
        int? parentNotificationId,
        CancellationToken cancellationToken)
    {
        var notification = new OrderNotification(orderId, buyerId, kind, destinationPhoneNumber, body, sendAt, parentNotificationId);

        try
        {
            var result = await _smsGateway.SendAsync(new SmsSendCommand(destinationPhoneNumber, body, sendAt), cancellationToken);
            if (result.Accepted && result.Message != null)
            {
                notification.ApplyProviderAcceptance(
                    result.Message.Sid,
                    result.Message.Status,
                    result.Message.ErrorCode,
                    result.Message.DateCreated,
                    result.Message.DateSent);
            }
            else
            {
                notification.MarkSendFailed(result.ErrorCode);
                _logger.LogWarning(
                    "Provider rejected {Kind} notification for order {OrderId} with error code {ErrorCode}.",
                    kind,
                    orderId,
                    result.ErrorCode?.ToString() ?? "none");
            }
        }
        catch (Exception ex)
        {
            notification.MarkSendFailed(null);
            _logger.LogWarning(
                "Failed to send {Kind} notification for order {OrderId}: {Message}",
                kind,
                orderId,
                PhoneNumberLogSanitizer.Redact(ex.Message));
        }

        return await _notifications.AddAsync(notification, cancellationToken);
    }

    private async Task RefreshFromProviderAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(notification.ProviderSid))
        {
            return;
        }

        try
        {
            var snapshot = await _smsGateway.FetchAsync(notification.ProviderSid, cancellationToken);
            if (snapshot == null)
            {
                return;
            }

            notification.ApplyProviderSnapshot(snapshot.Status, snapshot.ErrorCode, snapshot.Body, snapshot.DateCreated, snapshot.DateSent);
            if (notification.ContentRedacted)
            {
                notification.MarkRedacted();
            }

            await _notifications.UpdateAsync(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Failed to refresh notification {NotificationId} from provider: {Message}",
                notification.Id,
                PhoneNumberLogSanitizer.Redact(ex.Message));
        }
    }

    private async Task<string?> ResolveResendDestinationAsync(OrderNotification original, CancellationToken cancellationToken)
    {
        if (await _contactNumbers.IsRegisteredAsync(original.BuyerId, original.DestinationPhoneNumber, cancellationToken))
        {
            return original.DestinationPhoneNumber;
        }

        var primary = await _contactNumbers.GetPrimaryForBuyerAsync(original.BuyerId, cancellationToken);
        return primary?.PhoneNumber;
    }

    private static string BuildFallbackBody(OrderNotification original) => original.Kind switch
    {
        OrderNotificationKind.OrderPlaced => $"eShopOnWeb: Your order #{original.OrderId} has been placed. Thank you for your purchase.",
        OrderNotificationKind.OrderDispatched => $"eShopOnWeb: Your order #{original.OrderId} has been dispatched and is on its way.",
        OrderNotificationKind.DeliveryFeedback => $"eShopOnWeb: How did the delivery of order #{original.OrderId} go? We would love to hear from you.",
        OrderNotificationKind.OrderCancelled => $"eShopOnWeb: Your order #{original.OrderId} has been cancelled.",
        _ => $"eShopOnWeb: An update about order #{original.OrderId}."
    };
}
