using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationOrchestrator : IOrderNotificationOrchestrator
{
    public static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<NotificationResendAttempt> _resendAttempts;
    private readonly ISmsGateway _sms;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderNotificationOrchestrator> _logger;

    public OrderNotificationOrchestrator(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IRepository<ContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        IRepository<NotificationResendAttempt> resendAttempts,
        ISmsGateway sms,
        IUriComposer uriComposer,
        IAppLogger<OrderNotificationOrchestrator> logger)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _resendAttempts = resendAttempts;
        _sms = sms;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<Result<Order>> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<PlaceOrderItem> items,
        PlaceOrderAddress? address,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            return Result<Order>.Unauthorized();
        }

        var validation = ValidateItems(items);
        if (!validation.IsSuccess)
        {
            return Result<Order>.Invalid(validation.ValidationErrors.ToList());
        }

        var catalogIds = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(catalogIds), cancellationToken);
        if (catalogItems.Count != catalogIds.Length)
        {
            return ResultFactory.Invalid<Order>("items", "One or more catalog items were not found.");
        }

        var orderItems = items.Select(item =>
        {
            var catalogItem = catalogItems.First(c => c.Id == item.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, item.Quantity);
        }).ToList();

        var shipTo = address is null
            ? new Address("123 Main Street", "Seattle", "WA", "USA", "98101")
            : new Address(address.Street, address.City, address.State, address.Country, address.ZipCode);

        var order = new Order(buyerId, shipTo, orderItems);
        await _orders.AddAsync(order, cancellationToken);

        await NotifyAsync(
            order,
            OrderNotificationKind.OrderPlaced,
            $"Your eShopOnWeb order {order.Id} has been placed. Thank you for your purchase.",
            sendAt: null,
            cancellationToken);

        return Result<Order>.Success(order);
    }

    public async Task<Result<Order>> DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            return Result<Order>.NotFound();
        }

        try
        {
            order.MarkDispatched();
        }
        catch (InvalidOperationException ex)
        {
            throw new Microsoft.eShopWeb.ApplicationCore.Exceptions.ResourceConflictException(ex.Message);
        }

        await _orders.UpdateAsync(order, cancellationToken);

        await NotifyAsync(
            order,
            OrderNotificationKind.OrderDispatched,
            $"Your eShopOnWeb order {order.Id} is on its way.",
            sendAt: null,
            cancellationToken);

        await NotifyAsync(
            order,
            OrderNotificationKind.DeliveryFollowUp,
            $"How did the delivery of your eShopOnWeb order {order.Id} go? We would love to hear from you.",
            sendAt: DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay),
            cancellationToken);

        return Result<Order>.Success(order);
    }

    public async Task<Result<Order>> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            return Result<Order>.NotFound();
        }

        try
        {
            order.Cancel();
        }
        catch (InvalidOperationException ex)
        {
            throw new Microsoft.eShopWeb.ApplicationCore.Exceptions.ResourceConflictException(ex.Message);
        }

        await _orders.UpdateAsync(order, cancellationToken);

        await CancelScheduledFollowUpsAsync(order.Id, cancellationToken);

        await NotifyAsync(
            order,
            OrderNotificationKind.OrderCancelled,
            $"Your eShopOnWeb order {order.Id} has been cancelled.",
            sendAt: null,
            cancellationToken);

        return Result<Order>.Success(order);
    }

    public async Task<IReadOnlyList<Order>> ListBuyerOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            return new List<Order>();
        }

        return await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
    }

    public async Task<Result<IReadOnlyList<OrderNotification>>> ListOrderNotificationsAsync(
        int orderId,
        string buyerId,
        bool isAdministrator,
        CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            return Result<IReadOnlyList<OrderNotification>>.NotFound();
        }

        if (!isAdministrator && order.BuyerId != buyerId)
        {
            return Result<IReadOnlyList<OrderNotification>>.NotFound();
        }

        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderSpecification(orderId), cancellationToken);
        await RefreshFromProviderAsync(notifications, cancellationToken);
        return Result<IReadOnlyList<OrderNotification>>.Success(notifications);
    }

    public async Task<IReadOnlyList<OrderNotification>> ListNotificationsForOrdersAsync(
        IEnumerable<int> orderIds,
        CancellationToken cancellationToken = default)
    {
        var ids = orderIds.ToList();
        if (ids.Count == 0)
        {
            return Array.Empty<OrderNotification>();
        }

        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderIdsSpecification(ids), cancellationToken);
        await RefreshFromProviderAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<Result<OrderNotification>> ResendAsync(
        int notificationId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return ResultFactory.Invalid<OrderNotification>("idempotencyKey", "An idempotency key is required.");
        }

        var source = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (source is null)
        {
            return Result<OrderNotification>.NotFound();
        }

        var existingAttempt = await _resendAttempts.FirstOrDefaultAsync(
            new NotificationResendAttemptSpecification(notificationId, idempotencyKey),
            cancellationToken);
        if (existingAttempt is not null)
        {
            var existingNotification = await _notifications.GetByIdAsync(existingAttempt.ResultNotificationId, cancellationToken);
            if (existingNotification is not null)
            {
                await RefreshFromProviderAsync(new[] { existingNotification }, cancellationToken);
                return Result<OrderNotification>.Success(existingNotification);
            }
        }

        if (source.ProviderStatus is "delivered")
        {
            throw new Microsoft.eShopWeb.ApplicationCore.Exceptions.ResourceConflictException("This notification already reached the shopper.");
        }

        var stillRegistered = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndPhoneSpecification(source.BuyerId, source.DestinationNumber),
            cancellationToken);
        if (stillRegistered is null)
        {
            throw new Microsoft.eShopWeb.ApplicationCore.Exceptions.ResourceConflictException("The destination number is no longer on file for this shopper.");
        }

        var body = source.ContentRedacted || string.IsNullOrEmpty(source.Body)
            ? FallbackBody(source.Kind, source.OrderId)
            : source.Body;

        var resent = new OrderNotification(
            source.OrderId,
            source.BuyerId,
            source.Kind,
            source.DestinationNumber,
            body,
            scheduledFor: null,
            resentFromNotificationId: source.Id);

        await SendAndPersistAsync(resent, sendAt: null, cancellationToken);

        await _resendAttempts.AddAsync(
            new NotificationResendAttempt(source.Id, idempotencyKey, resent.Id),
            cancellationToken);

        return Result<OrderNotification>.Success(resent);
    }

    public async Task<Result> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            return Result.NotFound();
        }

        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            try
            {
                var snapshot = await _sms.RedactBodyAsync(notification.ProviderMessageSid, cancellationToken);
                ApplySnapshot(notification, snapshot);
            }
            catch
            {
                _logger.LogWarning(
                    "Provider content disposal failed for notification {NotificationId}.",
                    notification.Id);
                return Result.Error("The provider could not dispose of the message content.");
            }
        }

        notification.MarkContentRedacted();
        await _notifications.UpdateAsync(notification, cancellationToken);
        return Result.Success();
    }

    public async Task<Result<ReconciliationReport>> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (from > to)
        {
            return ResultFactory.Invalid<ReconciliationReport>("from", "'from' must be earlier than or equal to 'to'.");
        }

        IReadOnlyList<SmsMessageSnapshot> providerMessages;
        try
        {
            providerMessages = await _sms.ListSentFromConfiguredSenderAsync(from, to, cancellationToken);
        }
        catch
        {
            _logger.LogWarning("Provider reconciliation listing failed for the requested range.");
            return Result<ReconciliationReport>.Error("The messaging provider could not be queried for reconciliation.");
        }

        var fromNumber = _sms.ConfiguredFromNumber;

        var eshopInRange = await _notifications.ListAsync(
            new OrderNotificationsInDateRangeSpecification(from, to),
            cancellationToken);

        var providerSids = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.ProviderMessageSid))
            .Select(m => m.ProviderMessageSid!)
            .Distinct()
            .ToList();

        var eshopBySid = (await _notifications.ListAsync(
                new OrderNotificationsByProviderSidsSpecification(providerSids),
                cancellationToken))
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var local in eshopInRange)
        {
            if (!string.IsNullOrEmpty(local.ProviderMessageSid) && !eshopBySid.ContainsKey(local.ProviderMessageSid))
            {
                eshopBySid[local.ProviderMessageSid] = local;
            }
        }

        var entries = new List<ReconciliationEntry>();
        var matched = 0;
        var providerOnly = 0;
        var eshopOnly = 0;
        var seenSids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var provider in providerMessages)
        {
            if (string.IsNullOrEmpty(provider.ProviderMessageSid))
            {
                continue;
            }

            seenSids.Add(provider.ProviderMessageSid);
            if (eshopBySid.TryGetValue(provider.ProviderMessageSid, out var local))
            {
                matched++;
                entries.Add(new ReconciliationEntry(
                    provider.ProviderMessageSid,
                    local.Id,
                    "matched",
                    provider.Status,
                    local.ProviderStatus,
                    provider.DateSent,
                    local.CreatedAt));
            }
            else
            {
                providerOnly++;
                entries.Add(new ReconciliationEntry(
                    provider.ProviderMessageSid,
                    null,
                    "providerOnly",
                    provider.Status,
                    null,
                    provider.DateSent,
                    null));
            }
        }

        foreach (var local in eshopInRange)
        {
            if (string.IsNullOrEmpty(local.ProviderMessageSid) || !seenSids.Contains(local.ProviderMessageSid))
            {
                eshopOnly++;
                entries.Add(new ReconciliationEntry(
                    local.ProviderMessageSid,
                    local.Id,
                    "eshopOnly",
                    null,
                    local.ProviderStatus,
                    local.ProviderDateSent,
                    local.CreatedAt));
            }
        }

        return Result<ReconciliationReport>.Success(new ReconciliationReport(
            from,
            to,
            fromNumber,
            entries,
            matched,
            providerOnly,
            eshopOnly));
    }

    private static Result ValidateItems(IReadOnlyList<PlaceOrderItem> items)
    {
        if (items is null || items.Count == 0)
        {
            return ResultFactory.Invalid("items", "At least one catalog item is required.");
        }

        foreach (var item in items)
        {
            if (item.CatalogItemId <= 0)
            {
                return ResultFactory.Invalid("catalogItemId", "Catalog item id must be a positive integer.");
            }

            if (item.Quantity <= 0)
            {
                return ResultFactory.Invalid("quantity", "Quantity must be greater than zero.");
            }
        }

        return Result.Success();
    }

    private async Task NotifyAsync(
        Order order,
        OrderNotificationKind kind,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ContactNumber> destinations;
        try
        {
            destinations = await _contactNumbers.ListAsync(
                new ContactNumbersByBuyerSpecification(order.BuyerId),
                cancellationToken);
        }
        catch
        {
            _logger.LogWarning("Could not load contact numbers while notifying for order {OrderId}.", order.Id);
            return;
        }

        if (destinations.Count == 0)
        {
            return;
        }

        foreach (var destination in destinations)
        {
            var notification = new OrderNotification(
                order.Id,
                order.BuyerId,
                kind,
                destination.PhoneNumber,
                body,
                sendAt);

            await SendAndPersistAsync(notification, sendAt, cancellationToken);
        }
    }

    private async Task SendAndPersistAsync(
        OrderNotification notification,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await _sms.SendAsync(
                new SendSmsRequest(notification.DestinationNumber, notification.Body ?? string.Empty, sendAt),
                cancellationToken);
            ApplySnapshot(notification, snapshot);
        }
        catch
        {
            _logger.LogWarning(
                "Messaging provider call failed for order {OrderId} notification kind {Kind}.",
                notification.OrderId,
                notification.Kind);
            notification.MarkSendFailed("The messaging provider could not accept the message.");
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

    private async Task CancelScheduledFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var pending = await _notifications.ListAsync(new ScheduledFollowUpsByOrderSpecification(orderId), cancellationToken);
        foreach (var followUp in pending)
        {
            if (string.IsNullOrEmpty(followUp.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var snapshot = await _sms.CancelScheduledAsync(followUp.ProviderMessageSid, cancellationToken);
                ApplySnapshot(followUp, snapshot);
                await _notifications.UpdateAsync(followUp, cancellationToken);
            }
            catch
            {
                _logger.LogWarning(
                    "Could not cancel a scheduled follow-up for order {OrderId} notification {NotificationId}.",
                    orderId,
                    followUp.Id);
            }
        }
    }

    private async Task RefreshFromProviderAsync(
        IReadOnlyList<OrderNotification> notifications,
        CancellationToken cancellationToken)
    {
        var pending = notifications.Where(n =>
            !string.IsNullOrEmpty(n.ProviderMessageSid) && !n.IsTerminalStatus()).ToList();

        foreach (var notification in pending)
        {
            try
            {
                var snapshot = await _sms.GetAsync(notification.ProviderMessageSid!, cancellationToken);
                ApplySnapshot(notification, snapshot);
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch
            {
                _logger.LogWarning("Could not refresh provider status for notification {NotificationId}.", notification.Id);
            }
        }
    }

    private static void ApplySnapshot(OrderNotification notification, SmsMessageSnapshot snapshot)
    {
        notification.RecordProviderResult(
            snapshot.ProviderMessageSid,
            string.IsNullOrWhiteSpace(snapshot.Status) ? "unknown" : snapshot.Status,
            snapshot.ErrorCode,
            snapshot.ErrorMessage,
            snapshot.DateSent,
            notification.ContentRedacted ? string.Empty : snapshot.Body);
    }

    private static string FallbackBody(OrderNotificationKind kind, int orderId) => kind switch
    {
        OrderNotificationKind.OrderPlaced =>
            $"Your eShopOnWeb order {orderId} has been placed. Thank you for your purchase.",
        OrderNotificationKind.OrderDispatched =>
            $"Your eShopOnWeb order {orderId} is on its way.",
        OrderNotificationKind.DeliveryFollowUp =>
            $"How did the delivery of your eShopOnWeb order {orderId} go? We would love to hear from you.",
        OrderNotificationKind.OrderCancelled =>
            $"Your eShopOnWeb order {orderId} has been cancelled.",
        _ => $"An update about your eShopOnWeb order {orderId}."
    };
}
