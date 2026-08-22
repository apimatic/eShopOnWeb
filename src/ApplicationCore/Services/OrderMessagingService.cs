using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.Messaging;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderMessagingService : IOrderMessagingService
{
    public static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private static readonly Address DefaultShippingAddress =
        new("123 Main Street", "Seattle", "WA", "USA", "98101");

    private static readonly HashSet<string> DidNotReachStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "failed",
        "undelivered"
    };

    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<NotificationResendKey> _resendKeys;
    private readonly ISmsProvider _smsProvider;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderMessagingService> _logger;

    public OrderMessagingService(
        IRepository<ContactNumber> contactNumbers,
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IRepository<OrderNotification> notifications,
        IRepository<NotificationResendKey> resendKeys,
        ISmsProvider smsProvider,
        IUriComposer uriComposer,
        IAppLogger<OrderMessagingService> logger)
    {
        _contactNumbers = contactNumbers;
        _orders = orders;
        _catalogItems = catalogItems;
        _notifications = notifications;
        _resendKeys = resendKeys;
        _smsProvider = smsProvider;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<ContactNumber> RegisterContactNumberAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(phoneNumber, nameof(phoneNumber));

        var lookup = await _smsProvider.LookupAsync(phoneNumber, cancellationToken);
        if (!PhoneNumberUsability.IsUsableDestination(lookup, out var reason))
        {
            throw new BadRequestException(reason);
        }

        var canonical = lookup.CanonicalPhoneNumber!;
        var existing = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndPhoneSpecification(buyerId, canonical), cancellationToken);
        if (existing is not null)
        {
            throw new DuplicateException("This mobile number is already registered.");
        }

        var contact = new ContactNumber(buyerId, canonical, lookup.NationalFormat, lookup.LineType);
        return await _contactNumbers.AddAsync(contact, cancellationToken);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListContactNumbersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task DeleteContactNumberAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var contact = await _contactNumbers.GetByIdAsync(contactNumberId, cancellationToken);
        if (contact is null || contact.BuyerId != buyerId)
        {
            throw new EntityNotFoundException("Contact number not found.");
        }

        await _contactNumbers.DeleteAsync(contact, cancellationToken);
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> items, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (items is null || items.Count == 0)
        {
            throw new BadRequestException("An order must contain at least one catalog item.");
        }

        foreach (var line in items)
        {
            if (line.CatalogItemId <= 0 || line.Quantity <= 0)
            {
                throw new BadRequestException("Each item must include a catalog item id and a quantity greater than zero.");
            }
        }

        var catalogIds = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(catalogIds), cancellationToken);
        if (catalogItems.Count != catalogIds.Length)
        {
            throw new BadRequestException("One or more catalog items were not found.");
        }

        var quantities = items
            .GroupBy(i => i.CatalogItemId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));

        var orderItems = catalogItems.Select(catalogItem =>
        {
            var pictureUri = _uriComposer.ComposePicUri(catalogItem.PictureUri);
            if (string.IsNullOrEmpty(pictureUri))
            {
                pictureUri = "placeholder";
            }

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            return new OrderItem(itemOrdered, catalogItem.Price, quantities[catalogItem.Id]);
        }).ToList();

        var order = new Order(buyerId, DefaultShippingAddress, orderItems);
        await _orders.AddAsync(order, cancellationToken);

        await TryNotifyAsync(order, OrderNotificationKind.OrderPlaced,
            $"Your eShopOnWeb order #{order.Id} has been placed.", sendAt: null, cancellationToken);

        return order;
    }

    public async Task DispatchOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        try
        {
            order.MarkDispatched();
        }
        catch (InvalidOperationException ex)
        {
            throw new ConflictException(ex.Message);
        }

        await _orders.UpdateAsync(order, cancellationToken);

        await TryNotifyAsync(order, OrderNotificationKind.OrderDispatched,
            $"Your eShopOnWeb order #{order.Id} has been dispatched and is on its way.", sendAt: null, cancellationToken);

        await TryNotifyAsync(order, OrderNotificationKind.DeliveryFollowUp,
            $"How did the delivery of your eShopOnWeb order #{order.Id} go?",
            DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay), cancellationToken);
    }

    public async Task CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        try
        {
            order.MarkCancelled();
        }
        catch (InvalidOperationException ex)
        {
            throw new ConflictException(ex.Message);
        }

        await _orders.UpdateAsync(order, cancellationToken);

        await TryNotifyAsync(order, OrderNotificationKind.OrderCancelled,
            $"Your eShopOnWeb order #{order.Id} has been cancelled.", sendAt: null, cancellationToken);

        await CancelPendingFollowUpsAsync(order.Id, cancellationToken);
    }

    public async Task<IReadOnlyList<ShopperOrder>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        if (orders.Count == 0)
        {
            return Array.Empty<ShopperOrder>();
        }

        var notifications = await _notifications.ListAsync(
            new OrderNotificationsByOrderIdsSpecification(orders.Select(o => o.Id)), cancellationToken);
        await RefreshProviderStatusesAsync(notifications, cancellationToken);

        var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => (IReadOnlyList<OrderNotification>)g.ToList());
        return orders
            .Select(order => new ShopperOrder(order, byOrder.GetValueOrDefault(order.Id) ?? Array.Empty<OrderNotification>()))
            .ToList();
    }

    public async Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(int orderId, string? shopperBuyerId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        if (shopperBuyerId is not null && order.BuyerId != shopperBuyerId)
        {
            throw new EntityNotFoundException("Order not found.");
        }

        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderIdSpecification(orderId), cancellationToken);
        await RefreshProviderStatusesAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var existingKey = await _resendKeys.FirstOrDefaultAsync(
            new NotificationResendKeySpecification(notificationId, idempotencyKey), cancellationToken);
        if (existingKey is not null && existingKey.ResultNotificationId > 0)
        {
            var previous = await _notifications.GetByIdAsync(existingKey.ResultNotificationId, cancellationToken);
            if (previous is not null)
            {
                await RefreshProviderStatusesAsync(new OrderNotification[] { previous }, cancellationToken);
                return previous;
            }
        }

        var source = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new EntityNotFoundException("Notification not found.");

        await RefreshProviderStatusesAsync(new OrderNotification[] { source }, cancellationToken);

        if (source.ContentRedacted || string.IsNullOrEmpty(source.Body))
        {
            throw new ConflictException("The original message content has been disposed of and cannot be resent.");
        }

        if (!DidNotReach(source))
        {
            throw new ConflictException("Only messages that did not reach the shopper can be resent.");
        }

        var destination = await ResolveSendableDestinationAsync(source.BuyerId, source.DestinationNumber, cancellationToken);
        if (destination is null)
        {
            throw new ConflictException("The original destination is no longer on file and the shopper has no other number to message.");
        }

        if (existingKey is null)
        {
            existingKey = new NotificationResendKey(notificationId, idempotencyKey);
            await _resendKeys.AddAsync(existingKey, cancellationToken);
        }

        var resent = new OrderNotification(
            source.OrderId,
            source.BuyerId,
            source.Kind,
            destination,
            source.Body,
            sendAt: null,
            resentFromNotificationId: source.Id);

        await _notifications.AddAsync(resent, cancellationToken);
        await SendRecordedNotificationAsync(resent, cancellationToken);

        existingKey.AssignResult(resent.Id);
        await _resendKeys.UpdateAsync(existingKey, cancellationToken);

        return resent;
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new EntityNotFoundException("Notification not found.");

        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            try
            {
                var updated = await _smsProvider.RedactBodyAsync(notification.ProviderMessageSid, cancellationToken);
                notification.ApplyProviderState(updated.Sid, updated.Status ?? notification.ProviderStatus, updated.ErrorCode);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to redact provider content for notification {NotificationId}: {Message}", notification.Id, ex.Message);
                throw;
            }
        }

        notification.RedactBody();
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new BadRequestException("'to' must be on or after 'from'.");
        }

        var providerMessages = await _smsProvider.ListFromSenderAsync(from, to, cancellationToken);
        var local = await _notifications.ListAsync(new OrderNotificationsInRangeSpecification(from, to), cancellationToken);

        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid))
            .GroupBy(m => m.Sid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var extraLocalSids = providerBySid.Keys
            .Where(sid => local.All(n => n.ProviderMessageSid != sid))
            .ToArray();
        if (extraLocalSids.Length > 0)
        {
            var extras = await _notifications.ListAsync(new OrderNotificationsByProviderSidsSpecification(extraLocalSids), cancellationToken);
            local = local.Concat(extras).GroupBy(n => n.Id).Select(g => g.First()).ToList();
        }

        var localBySid = local
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new List<ReconciliationEntry>();
        var inProviderOnly = new List<ReconciliationEntry>();
        var inEShopOnly = new List<ReconciliationEntry>();

        foreach (var provider in providerBySid.Values)
        {
            if (localBySid.TryGetValue(provider.Sid!, out var notification))
            {
                matched.Add(new ReconciliationEntry(notification.Id, provider.Sid, provider.Status, provider.DateSent, "matched"));
            }
            else
            {
                inProviderOnly.Add(new ReconciliationEntry(null, provider.Sid, provider.Status, provider.DateSent, "provider-only"));
            }
        }

        foreach (var notification in local)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid) || !providerBySid.ContainsKey(notification.ProviderMessageSid))
            {
                inEShopOnly.Add(new ReconciliationEntry(notification.Id, notification.ProviderMessageSid, notification.ProviderStatus, null, "eshop-only"));
            }
        }

        return new ReconciliationReport(from, to, _smsProvider.SendingNumber, matched, inProviderOnly, inEShopOnly);
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new EntityNotFoundException("Order not found.");
        }

        return order;
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
            var destination = await GetCurrentDestinationAsync(order.BuyerId, cancellationToken);
            if (destination is null)
            {
                _logger.LogInformation("Skipping {Kind} notification for order {OrderId}; shopper has no number on file.", kind, order.Id);
                return;
            }

            var notification = new OrderNotification(order.Id, order.BuyerId, kind, destination, body, sendAt);
            await _notifications.AddAsync(notification, cancellationToken);
            await SendRecordedNotificationAsync(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to send {Kind} notification for order {OrderId}: {Message}", kind, order.Id, ex.Message);
        }
    }

    private async Task SendRecordedNotificationAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            var sent = await _smsProvider.SendAsync(
                new SendProviderMessageRequest(notification.DestinationNumber, notification.Body ?? string.Empty, notification.SendAt),
                cancellationToken);
            notification.ApplyProviderState(sent.Sid, sent.Status ?? "queued", sent.ErrorCode);
        }
        catch (Exception ex)
        {
            notification.MarkSendFailed();
            _logger.LogWarning("Provider rejected {Kind} notification {NotificationId}: {Message}", notification.Kind, notification.Id, ex.Message);
        }

        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var followUps = await _notifications.ListAsync(new ScheduledFollowUpNotificationsSpecification(orderId), cancellationToken);
        foreach (var followUp in followUps)
        {
            try
            {
                await RefreshProviderStatusesAsync(new OrderNotification[] { followUp }, cancellationToken);
                if (!string.Equals(followUp.ProviderStatus, "scheduled", StringComparison.OrdinalIgnoreCase) ||
                    string.IsNullOrEmpty(followUp.ProviderMessageSid))
                {
                    continue;
                }

                var updated = await _smsProvider.CancelAsync(followUp.ProviderMessageSid, cancellationToken);
                followUp.ApplyProviderState(updated.Sid, updated.Status ?? "canceled", updated.ErrorCode);
                await _notifications.UpdateAsync(followUp, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to cancel follow-up notification {NotificationId}: {Message}", followUp.Id, ex.Message);
            }
        }
    }

    private async Task RefreshProviderStatusesAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var current = await _smsProvider.FetchAsync(notification.ProviderMessageSid, cancellationToken);
                notification.ApplyProviderState(current.Sid, current.Status ?? notification.ProviderStatus, current.ErrorCode);
                if (notification.ContentRedacted)
                {
                    notification.RedactBody();
                }
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to refresh provider status for notification {NotificationId}: {Message}", notification.Id, ex.Message);
            }
        }
    }

    private async Task<string?> GetCurrentDestinationAsync(string buyerId, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return numbers.FirstOrDefault()?.PhoneNumber;
    }

    private async Task<string?> ResolveSendableDestinationAsync(string buyerId, string originalDestination, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        if (numbers.Count == 0)
        {
            return null;
        }

        var original = numbers.FirstOrDefault(n => n.PhoneNumber == originalDestination);
        return original?.PhoneNumber ?? numbers[0].PhoneNumber;
    }

    private static bool DidNotReach(OrderNotification notification)
    {
        if (string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            return true;
        }

        return DidNotReachStatuses.Contains(notification.ProviderStatus);
    }
}
