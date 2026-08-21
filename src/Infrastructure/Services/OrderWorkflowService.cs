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
using Microsoft.eShopWeb.Infrastructure.Twilio;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class OrderWorkflowService : IOrderWorkflowService
{
    public static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private static readonly Address DefaultShipToAddress =
        new("123 Main St.", "Kent", "OH", "United States", "44240");

    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly ITwilioMessagingClient _messaging;
    private readonly IUriComposer _uriComposer;
    private readonly TwilioOptions _twilioOptions;
    private readonly ILogger<OrderWorkflowService> _logger;

    public OrderWorkflowService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IRepository<ContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        ITwilioMessagingClient messaging,
        IUriComposer uriComposer,
        IOptions<TwilioOptions> twilioOptions,
        ILogger<OrderWorkflowService> logger)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _messaging = messaging;
        _uriComposer = uriComposer;
        _twilioOptions = twilioOptions.Value;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<PlaceOrderItem> items,
        Address? shipToAddress,
        CancellationToken cancellationToken = default)
    {
        if (items == null || items.Count == 0)
        {
            throw new ArgumentException("An order must contain at least one item.", nameof(items));
        }

        var ids = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        var catalogById = catalogItems.ToDictionary(c => c.Id);

        var orderItems = new List<OrderItem>();
        foreach (var item in items)
        {
            if (item.Quantity <= 0)
            {
                throw new ArgumentException("Quantity must be greater than zero.", nameof(items));
            }

            if (!catalogById.TryGetValue(item.CatalogItemId, out var catalogItem))
            {
                throw new EntityNotFoundException(nameof(CatalogItem), item.CatalogItemId);
            }

            var pictureUri = _uriComposer.ComposePicUri(catalogItem.PictureUri);
            if (string.IsNullOrEmpty(pictureUri))
            {
                pictureUri = "images/products/placeholder.png";
            }

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, item.Quantity));
        }

        var order = new Order(buyerId, shipToAddress ?? DefaultShipToAddress, orderItems);
        order = await _orders.AddAsync(order, cancellationToken);

        await TryNotifyAsync(
            order,
            NotificationKind.OrderPlaced,
            BuildBody(NotificationKind.OrderPlaced, order.Id),
            sendAt: null,
            cancellationToken);

        return order;
    }

    public async Task<Order> DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderOrThrow(orderId, cancellationToken);
        order.MarkDispatched();
        await _orders.UpdateAsync(order, cancellationToken);

        await TryNotifyAsync(
            order,
            NotificationKind.OrderDispatched,
            BuildBody(NotificationKind.OrderDispatched, order.Id),
            sendAt: null,
            cancellationToken);

        await TryNotifyAsync(
            order,
            NotificationKind.DeliveryFollowUp,
            BuildBody(NotificationKind.DeliveryFollowUp, order.Id),
            sendAt: DateTimeOffset.UtcNow.Add(FollowUpDelay),
            cancellationToken);

        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderOrThrow(orderId, cancellationToken);
        order.MarkCancelled();
        await _orders.UpdateAsync(order, cancellationToken);

        try
        {
            await CancelPendingFollowUpsAsync(order.Id, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to cancel follow-up messages for order {OrderId}.", order.Id);
        }

        await TryNotifyAsync(
            order,
            NotificationKind.OrderCancelled,
            BuildBody(NotificationKind.OrderCancelled, order.Id),
            sendAt: null,
            cancellationToken);

        return order;
    }

    public async Task<IReadOnlyList<Order>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orders.ListAsync(new CustomerOrdersSpecification(buyerId), cancellationToken);
        await SyncNotificationsAsync(orders.Select(o => o.Id), cancellationToken);
        return orders;
    }

    public async Task<IReadOnlyList<OrderNotification>> ListOrderNotificationsAsync(
        int orderId,
        string buyerId,
        CancellationToken cancellationToken = default)
    {
        var order = await GetOrderOrThrow(orderId, cancellationToken);
        if (order.BuyerId != buyerId)
        {
            throw new EntityNotFoundException(nameof(Order), orderId);
        }

        await SyncNotificationsAsync(new[] { orderId }, cancellationToken);
        return await _notifications.ListAsync(new OrderNotificationsByOrderIdSpecification(orderId), cancellationToken);
    }

    public async Task<OrderNotification> ResendAsync(
        int notificationId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("An idempotency key is required.", nameof(idempotencyKey));
        }

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(OrderNotification), notificationId);

        var existing = await _notifications.FirstOrDefaultAsync(
            new ResendByIdempotencySpecification(notificationId, idempotencyKey),
            cancellationToken);
        if (existing != null)
        {
            await RefreshOneAsync(existing, cancellationToken);
            return existing;
        }

        await GetOrderOrThrow(original.OrderId, cancellationToken);
        var body = original.ContentRedacted || string.IsNullOrEmpty(original.Body)
            ? BuildBody(original.Kind, original.OrderId)
            : original.Body;

        var resend = new OrderNotification(
            original.OrderId,
            original.BuyerId,
            original.Kind,
            body,
            original.ContactNumberId,
            original.DestinationNumber);
        resend.AssignResend(original.Id, idempotencyKey);

        var destination = await ResolveSendableDestinationAsync(original, cancellationToken);
        if (destination == null)
        {
            resend.MarkLocalFailure("skipped", "No registered destination is available for resend.");
            return await _notifications.AddAsync(resend, cancellationToken);
        }

        await SendAndPersistAsync(resend, destination, sendAt: null, cancellationToken);
        return resend;
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(OrderNotification), notificationId);

        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            try
            {
                var updated = await _messaging.UpdateMessageAsync(
                    notification.ProviderMessageSid,
                    body: string.Empty,
                    status: null,
                    cancellationToken);
                notification.ApplyProviderState(updated.Sid, updated.Status, updated.ErrorCode, updated.ErrorMessage);
            }
            catch (TwilioClientException ex)
            {
                _logger.LogWarning(
                    "Failed to redact provider content for notification {NotificationId} HTTP {StatusCode}.",
                    notification.Id,
                    ex.StatusCode);
                throw;
            }
        }

        notification.RedactContent();
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var fromNumber = _twilioOptions.FromNumber;
        var providerMessages = await _messaging.ListMessagesFromAsync(fromNumber, from, to, cancellationToken);
        var local = await _notifications.ListAsync(
            new OrderNotificationsCreatedBetweenSpecification(from, to),
            cancellationToken);

        var localBySid = local
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid))
            .GroupBy(m => m.Sid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var matches = new List<ReconciliationMatch>();
        var providerOnly = new List<ProviderMessage>();
        var applicationOnly = new List<OrderNotification>();

        foreach (var provider in providerMessages)
        {
            if (string.IsNullOrEmpty(provider.Sid))
            {
                providerOnly.Add(provider);
                continue;
            }

            if (localBySid.TryGetValue(provider.Sid, out var notification))
            {
                matches.Add(new ReconciliationMatch(notification, provider));
            }
            else
            {
                providerOnly.Add(provider);
            }
        }

        foreach (var notification in local)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid)
                || !providerBySid.ContainsKey(notification.ProviderMessageSid))
            {
                applicationOnly.Add(notification);
            }
        }

        return new ReconciliationReport(from, to, fromNumber, matches, providerOnly, applicationOnly);
    }

    private async Task<Order> GetOrderOrThrow(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order == null)
        {
            throw new EntityNotFoundException(nameof(Order), orderId);
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
            var destination = await GetCurrentDestinationAsync(order.BuyerId, cancellationToken);
            if (destination == null)
            {
                _logger.LogInformation("Skipping {Kind} notification for order {OrderId}; no number on file.", kind, order.Id);
                return;
            }

            var notification = new OrderNotification(
                order.Id,
                order.BuyerId,
                kind,
                body,
                destination.Id,
                destination.CanonicalNumber);

            if (sendAt.HasValue)
            {
                notification.MarkScheduled(sendAt.Value);
            }

            await SendAndPersistAsync(notification, destination.CanonicalNumber, sendAt, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Notification {Kind} for order {OrderId} failed; the order operation still succeeded.", kind, order.Id);
        }
    }

    private async Task SendAndPersistAsync(
        OrderNotification notification,
        string destination,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var sent = await _messaging.CreateMessageAsync(destination, notification.Body ?? string.Empty, sendAt, cancellationToken);
            notification.ApplyProviderState(sent.Sid, sent.Status, sent.ErrorCode, sent.ErrorMessage);
        }
        catch (TwilioClientException ex)
        {
            _logger.LogWarning(
                "CreateMessage for order {OrderId} kind {Kind} failed with HTTP {StatusCode}.",
                notification.OrderId,
                notification.Kind,
                ex.StatusCode);
            notification.MarkLocalFailure("failed", $"Provider rejected the send (HTTP {ex.StatusCode}).");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "CreateMessage for order {OrderId} kind {Kind} failed.", notification.OrderId, notification.Kind);
            notification.MarkLocalFailure("failed", "The message could not be sent.");
        }

        if (notification.Id == 0)
        {
            await _notifications.AddAsync(notification, cancellationToken);
        }
        else
        {
            await _notifications.UpdateAsync(notification, cancellationToken);
        }
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var pending = await _notifications.ListAsync(new PendingFollowUpByOrderSpecification(orderId), cancellationToken);
        foreach (var followUp in pending)
        {
            if (string.IsNullOrEmpty(followUp.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var updated = await _messaging.UpdateMessageAsync(
                    followUp.ProviderMessageSid,
                    body: null,
                    status: "canceled",
                    cancellationToken);
                var status = string.IsNullOrWhiteSpace(updated.Status) ? "canceled" : updated.Status;
                followUp.ApplyProviderState(updated.Sid, status, updated.ErrorCode, updated.ErrorMessage);
                if (!string.Equals(followUp.ProviderStatus, "canceled", StringComparison.OrdinalIgnoreCase))
                {
                    followUp.ApplyProviderState(followUp.ProviderMessageSid, "canceled", followUp.ProviderErrorCode, followUp.ProviderErrorMessage);
                }
            }
            catch (TwilioClientException ex)
            {
                _logger.LogWarning(
                    "Failed to cancel scheduled follow-up {NotificationId} HTTP {StatusCode}.",
                    followUp.Id,
                    ex.StatusCode);
                followUp.MarkLocalFailure(followUp.ProviderStatus ?? "scheduled", "Provider cancel request failed.");
            }

            await _notifications.UpdateAsync(followUp, cancellationToken);
        }
    }

    private async Task<ContactNumber?> GetCurrentDestinationAsync(string buyerId, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return numbers.FirstOrDefault();
    }

    private async Task<string?> ResolveSendableDestinationAsync(OrderNotification original, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(
            new ContactNumbersByBuyerSpecification(original.BuyerId),
            cancellationToken);
        if (numbers.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrEmpty(original.DestinationNumber)
            && numbers.Any(n => n.CanonicalNumber == original.DestinationNumber))
        {
            return original.DestinationNumber;
        }

        return null;
    }

    private async Task SyncNotificationsAsync(IEnumerable<int> orderIds, CancellationToken cancellationToken)
    {
        var ids = orderIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return;
        }

        var notifications = await _notifications.ListAsync(
            new OrderNotificationsByOrderIdsSpecification(ids),
            cancellationToken);

        foreach (var notification in notifications)
        {
            await RefreshOneAsync(notification, cancellationToken);
        }
    }

    private async Task RefreshOneAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            return;
        }

        try
        {
            var current = await _messaging.FetchMessageAsync(notification.ProviderMessageSid, cancellationToken);
            notification.ApplyProviderState(current.Sid, current.Status, current.ErrorCode, current.ErrorMessage);
            if (notification.ContentRedacted)
            {
                notification.RedactContent();
            }

            await _notifications.UpdateAsync(notification, cancellationToken);
        }
        catch (TwilioClientException ex)
        {
            _logger.LogWarning(
                "FetchMessage for notification {NotificationId} failed with HTTP {StatusCode}.",
                notification.Id,
                ex.StatusCode);
        }
    }

    internal static string BuildBody(NotificationKind kind, int orderId)
    {
        return kind switch
        {
            NotificationKind.OrderPlaced => $"eShopOnWeb: Your order #{orderId} has been placed. Thank you!",
            NotificationKind.OrderDispatched => $"eShopOnWeb: Your order #{orderId} is on its way.",
            NotificationKind.DeliveryFollowUp => $"eShopOnWeb: How did the delivery of order #{orderId} go? We would love your feedback.",
            NotificationKind.OrderCancelled => $"eShopOnWeb: Your order #{orderId} has been cancelled.",
            _ => $"eShopOnWeb: An update for order #{orderId}."
        };
    }
}
