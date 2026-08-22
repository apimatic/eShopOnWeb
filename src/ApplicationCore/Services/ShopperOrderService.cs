using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ShopperOrderService : IShopperOrderService, IOrderNotificationQueryService
{
    public static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private static readonly Address DefaultShippingAddress =
        new("123 Main St.", "Kent", "OH", "United States", "44240");

    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<NotificationResendKey> _resendKeys;
    private readonly ISmsMessagingService _messaging;
    private readonly IUriComposer _uriComposer;
    private readonly TwilioSettings _twilioSettings;
    private readonly ILogger<ShopperOrderService> _logger;

    public ShopperOrderService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IRepository<ContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        IRepository<NotificationResendKey> resendKeys,
        ISmsMessagingService messaging,
        IUriComposer uriComposer,
        IOptions<TwilioSettings> twilioOptions,
        ILogger<ShopperOrderService> logger)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _resendKeys = resendKeys;
        _messaging = messaging;
        _uriComposer = uriComposer;
        _twilioSettings = twilioOptions.Value;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<PlaceOrderItem> items, CancellationToken cancellationToken = default)
    {
        if (items.Count == 0)
        {
            throw new ArgumentException("An order must contain at least one item.");
        }

        var catalogIds = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(catalogIds), cancellationToken);
        var catalogById = catalogItems.ToDictionary(c => c.Id);

        var orderItems = new List<OrderItem>();
        foreach (var line in items)
        {
            if (line.Quantity <= 0)
            {
                throw new ArgumentException("Item quantities must be greater than zero.");
            }

            if (!catalogById.TryGetValue(line.CatalogItemId, out var catalogItem))
            {
                throw new CatalogItemNotFoundException(line.CatalogItemId);
            }

            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, DefaultShippingAddress, orderItems);
        await _orders.AddAsync(order, cancellationToken);

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
        var order = await _orders.GetByIdAsync(orderId, cancellationToken)
                    ?? throw new OrderNotFoundException(orderId);

        order.MarkDispatched();
        await _orders.UpdateAsync(order, cancellationToken);

        await TryNotifyAsync(
            order,
            OrderNotificationKind.OrderDispatched,
            $"eShopOnWeb: Your order #{order.Id} is on its way.",
            sendAt: null,
            cancellationToken);

        await TryNotifyAsync(
            order,
            OrderNotificationKind.DeliveryFollowUp,
            $"eShopOnWeb: How did the delivery of order #{order.Id} go? We would like to hear how it went.",
            sendAt: DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay),
            cancellationToken);

        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken)
                    ?? throw new OrderNotFoundException(orderId);

        order.MarkCancelled();
        await _orders.UpdateAsync(order, cancellationToken);

        var scheduled = await _notifications.ListAsync(new ScheduledFollowUpsByOrderSpecification(orderId), cancellationToken);
        foreach (var followUp in scheduled)
        {
            await TryCancelScheduledAsync(followUp, cancellationToken);
        }

        await TryNotifyAsync(
            order,
            OrderNotificationKind.OrderCancelled,
            $"eShopOnWeb: Your order #{order.Id} has been cancelled.",
            sendAt: null,
            cancellationToken);

        return order;
    }

    public async Task<IReadOnlyList<Order>> ListBuyerOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _orders.ListAsync(new CustomerOrdersSpecification(buyerId), cancellationToken);
    }

    public async Task<Order?> GetBuyerOrderAsync(int orderId, string buyerId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null || order.BuyerId != buyerId)
        {
            return null;
        }

        return order;
    }

    public async Task<IReadOnlyList<OrderNotification>> GetForOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var notifications = await _notifications.ListAsync(
            new OrderNotificationsByOrderIdSpecification(orderId), cancellationToken);

        foreach (var notification in notifications)
        {
            await SyncFromProviderAsync(notification, cancellationToken);
        }

        return notifications;
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("An idempotency key is required.");
        }

        var existingKey = await _resendKeys.FirstOrDefaultAsync(
            new NotificationResendKeySpecification(notificationId, idempotencyKey), cancellationToken);

        if (existingKey?.ResultNotificationId is int existingResultId)
        {
            var existingResult = await _notifications.GetByIdAsync(existingResultId, cancellationToken);
            if (existingResult is not null)
            {
                await SyncFromProviderAsync(existingResult, cancellationToken);
                return existingResult;
            }
        }

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken)
                       ?? throw new NotificationNotFoundException(notificationId);

        if (original.ContentRedacted || string.IsNullOrEmpty(original.Body))
        {
            throw new InvalidOrderStateException("The message content has been disposed of and cannot be re-sent.");
        }

        await SyncFromProviderAsync(original, cancellationToken);

        var eligibleForResend =
            original.ProviderMessageSid is null ||
            original.ProviderStatus is "failed" or "undelivered";

        if (!eligibleForResend)
        {
            throw new InvalidOrderStateException("Only messages that did not reach the shopper can be re-sent.");
        }

        var contact = await ResolveDestinationAsync(original.BuyerId, original.ContactNumberId, cancellationToken);
        if (contact is null)
        {
            throw new InvalidOrderStateException("The shopper has no registered mobile number to receive a re-send.");
        }

        NotificationResendKey resendKey;
        if (existingKey is null)
        {
            resendKey = new NotificationResendKey(notificationId, idempotencyKey);
            try
            {
                await _resendKeys.AddAsync(resendKey, cancellationToken);
            }
            catch (Exception)
            {
                var raced = await _resendKeys.FirstOrDefaultAsync(
                    new NotificationResendKeySpecification(notificationId, idempotencyKey), cancellationToken);
                if (raced?.ResultNotificationId is int racedId)
                {
                    var racedNotification = await _notifications.GetByIdAsync(racedId, cancellationToken);
                    if (racedNotification is not null)
                    {
                        return racedNotification;
                    }
                }

                throw;
            }
        }
        else
        {
            resendKey = existingKey;
        }

        var resent = new OrderNotification(
            original.OrderId,
            original.BuyerId,
            contact.Id,
            original.Kind,
            original.Body);

        await DispatchToProviderAsync(resent, contact.PhoneNumber, sendAt: null, cancellationToken);
        await _notifications.AddAsync(resent, cancellationToken);

        resendKey.AssignResult(resent.Id);
        await _resendKeys.UpdateAsync(resendKey, cancellationToken);

        return resent;
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken)
                           ?? throw new NotificationNotFoundException(notificationId);

        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            var updated = await _messaging.RedactBodyAsync(notification.ProviderMessageSid, cancellationToken);
            if (updated is not null)
            {
                notification.SyncFromProvider(
                    updated.Status ?? notification.ProviderStatus ?? "sent",
                    updated.ErrorCode,
                    string.Empty);
            }
        }

        notification.MarkContentRedacted();
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    public async Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new ArgumentException("The reconciliation 'to' value must be on or after 'from'.");
        }

        var fromNumber = _twilioSettings.FromNumber;
        var providerMessages = await _messaging.ListFromNumberAsync(fromNumber, from, to, cancellationToken);
        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid))
            .GroupBy(m => m.Sid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var localFromProvider = providerBySid.Count == 0
            ? new List<OrderNotification>()
            : await _notifications.ListAsync(new OrderNotificationsByProviderSidsSpecification(providerBySid.Keys), cancellationToken);

        var localInRange = await _notifications.ListAsync(
            new OrderNotificationsCreatedInRangeSpecification(from, to), cancellationToken);

        var localBySid = localFromProvider.Concat(localInRange)
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var localWithoutSid = localInRange.Where(n => string.IsNullOrEmpty(n.ProviderMessageSid)).ToList();

        var matched = new List<ReconciledMessage>();
        var providerOnly = new List<ReconciledMessage>();
        var applicationOnly = new List<ReconciledMessage>();

        foreach (var (sid, provider) in providerBySid)
        {
            if (localBySid.TryGetValue(sid, out var local))
            {
                matched.Add(ToReconciled(local, provider));
            }
            else
            {
                providerOnly.Add(new ReconciledMessage
                {
                    ProviderMessageSid = sid,
                    DeliveryStatus = provider.Status,
                    DateSent = provider.DateSent,
                    DateCreated = provider.DateCreated
                });
            }
        }

        foreach (var local in localBySid.Values)
        {
            if (!providerBySid.ContainsKey(local.ProviderMessageSid!))
            {
                applicationOnly.Add(ToReconciled(local, null));
            }
        }

        foreach (var local in localWithoutSid)
        {
            applicationOnly.Add(ToReconciled(local, null));
        }

        return new NotificationReconciliationReport(from, to, fromNumber, matched, providerOnly, applicationOnly);
    }

    private static ReconciledMessage ToReconciled(OrderNotification local, ProviderMessage? provider)
    {
        return new ReconciledMessage
        {
            NotificationId = local.Id,
            ProviderMessageSid = local.ProviderMessageSid ?? provider?.Sid,
            DeliveryStatus = provider?.Status ?? local.ProviderStatus,
            OrderId = local.OrderId,
            Kind = local.Kind.ToString(),
            DateSent = provider?.DateSent,
            DateCreated = provider?.DateCreated ?? local.CreatedAt
        };
    }

    private async Task TryNotifyAsync(
        Order order,
        OrderNotificationKind kind,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var contact = await ResolveDestinationAsync(order.BuyerId, contactNumberId: null, cancellationToken);
            if (contact is null)
            {
                return;
            }

            var notification = new OrderNotification(order.Id, order.BuyerId, contact.Id, kind, body);
            await DispatchToProviderAsync(notification, contact.PhoneNumber, sendAt, cancellationToken);
            await _notifications.AddAsync(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Order {OrderId} notification of kind {Kind} could not be sent.", order.Id, kind);
        }
    }

    private async Task DispatchToProviderAsync(
        OrderNotification notification,
        string destination,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _messaging.SendAsync(
                new CreateProviderMessageRequest
                {
                    To = destination,
                    Body = notification.Body!,
                    SendAt = sendAt
                },
                cancellationToken);

            if (result.Accepted && result.Message?.Sid is not null)
            {
                notification.RecordProviderAcceptance(
                    result.Message.Sid,
                    result.Message.Status ?? (sendAt.HasValue ? "scheduled" : "queued"),
                    sendAt);
            }
            else
            {
                notification.RecordProviderFailure(result.ErrorStatus, result.ErrorCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Provider send failed for order {OrderId} kind {Kind}.", notification.OrderId, notification.Kind);
            notification.RecordProviderFailure("failed", null);
        }
    }

    private async Task TryCancelScheduledAsync(OrderNotification followUp, CancellationToken cancellationToken)
    {
        try
        {
            var updated = await _messaging.CancelAsync(followUp.ProviderMessageSid!, cancellationToken);
            if (updated is not null)
            {
                followUp.SyncFromProvider(updated.Status ?? "canceled", updated.ErrorCode, updated.Body);
            }
            else
            {
                followUp.RecordProviderFailure("canceled", null);
            }

            await _notifications.UpdateAsync(followUp, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cancel scheduled follow-up {NotificationId} for order {OrderId}.", followUp.Id, followUp.OrderId);
        }
    }

    private async Task SyncFromProviderAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            return;
        }

        try
        {
            var current = await _messaging.FetchAsync(notification.ProviderMessageSid, cancellationToken);
            if (current?.Status is null)
            {
                return;
            }

            notification.SyncFromProvider(current.Status, current.ErrorCode, current.Body);
            await _notifications.UpdateAsync(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not refresh provider status for notification {NotificationId}.", notification.Id);
        }
    }

    private async Task<ContactNumber?> ResolveDestinationAsync(string buyerId, int? contactNumberId, CancellationToken cancellationToken)
    {
        if (contactNumberId.HasValue)
        {
            var specific = await _contactNumbers.GetByIdAsync(contactNumberId.Value, cancellationToken);
            if (specific is not null && specific.BuyerId == buyerId)
            {
                return specific;
            }
        }

        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return numbers.FirstOrDefault();
    }
}
