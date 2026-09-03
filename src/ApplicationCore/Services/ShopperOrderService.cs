using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ShopperOrderService : IShopperOrderService
{
    public static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private static readonly Address DefaultShipToAddress =
        new("123 Main St.", "Kent", "OH", "United States", "44240");

    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<ShopperContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IUriComposer _uriComposer;
    private readonly ISmsGateway _sms;
    private readonly IAppLogger<ShopperOrderService> _logger;

    public ShopperOrderService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IRepository<ShopperContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        IUriComposer uriComposer,
        ISmsGateway sms,
        IAppLogger<ShopperOrderService> logger)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _uriComposer = uriComposer;
        _sms = sms;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<CatalogOrderLine> lines, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
        {
            throw new CatalogOrderException("At least one catalog item is required.");
        }

        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new CatalogOrderException("Quantity must be greater than zero.");
            }
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        if (catalogItems.Count != ids.Length)
        {
            throw new EntityNotFoundException("One or more catalog items were not found.");
        }

        var orderItems = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var pictureUri = _uriComposer.ComposePicUri(catalogItem.PictureUri);
            if (string.IsNullOrEmpty(pictureUri))
            {
                pictureUri = "placeholder";
            }

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, DefaultShipToAddress, orderItems);
        await _orders.AddAsync(order, cancellationToken);

        await TryNotifyAsync(
            order,
            OrderNotificationKind.OrderPlaced,
            $"Your eShopOnWeb order #{order.Id} has been placed.",
            scheduleAt: null,
            cancellationToken);

        return order;
    }

    public async Task<IReadOnlyList<ShopperOrderSummary>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        if (orders.Count == 0)
        {
            return Array.Empty<ShopperOrderSummary>();
        }

        var notifications = await _notifications.ListAsync(
            new OrderNotificationsByOrderIdsSpecification(orders.Select(o => o.Id)),
            cancellationToken);

        await RefreshNonTerminalAsync(notifications, cancellationToken);

        var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => (IReadOnlyList<OrderNotification>)g.ToList());

        return orders
            .OrderByDescending(o => o.Id)
            .Select(o => new ShopperOrderSummary
            {
                Order = o,
                Notifications = byOrder.TryGetValue(o.Id, out var list) ? list : Array.Empty<OrderNotification>()
            })
            .ToList();
    }

    public async Task DispatchAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        order.MarkDispatched(DateTimeOffset.UtcNow);
        await _orders.UpdateAsync(order, cancellationToken);

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(20));

        await TryNotifyAsync(
            order,
            OrderNotificationKind.OrderDispatched,
            $"Your eShopOnWeb order #{order.Id} is on its way.",
            scheduleAt: null,
            budget.Token);

        await TryNotifyAsync(
            order,
            OrderNotificationKind.DeliveryFollowUp,
            $"How did the delivery of eShopOnWeb order #{order.Id} go?",
            scheduleAt: DateTimeOffset.UtcNow.Add(FollowUpDelay),
            budget.Token);
    }

    public async Task CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        order.MarkCancelled(DateTimeOffset.UtcNow);
        await _orders.UpdateAsync(order, cancellationToken);

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(20));

        var scheduled = await _notifications.ListAsync(new ScheduledFollowUpByOrderSpecification(order.Id), budget.Token);
        foreach (var followUp in scheduled)
        {
            if (string.IsNullOrWhiteSpace(followUp.ProviderSid))
            {
                followUp.RefreshFromProvider("canceled", null, "Cancelled locally before the provider accepted it.");
                await _notifications.UpdateAsync(followUp, budget.Token);
                continue;
            }

            var cancelResult = await _sms.CancelScheduledAsync(followUp.ProviderSid, budget.Token);
            if (cancelResult.ProviderAccepted)
            {
                followUp.RefreshFromProvider(
                    cancelResult.Status ?? "canceled",
                    cancelResult.ErrorCode,
                    cancelResult.ErrorMessage);
            }
            else
            {
                _logger.LogWarning(
                    "Could not cancel scheduled follow-up {NotificationId} for order {OrderId}.",
                    followUp.Id,
                    order.Id);
                if (!string.IsNullOrWhiteSpace(cancelResult.Status))
                {
                    followUp.RefreshFromProvider(cancelResult.Status, cancelResult.ErrorCode, cancelResult.ErrorMessage);
                }
            }

            await _notifications.UpdateAsync(followUp, budget.Token);
        }

        await TryNotifyAsync(
            order,
            OrderNotificationKind.OrderCancelled,
            $"Your eShopOnWeb order #{order.Id} has been cancelled.",
            scheduleAt: null,
            budget.Token);
    }

    public async Task<IReadOnlyList<OrderNotification>> ListNotificationsAsync(
        int orderId,
        string buyerId,
        bool isAdministrator,
        CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        if (!isAdministrator && order.BuyerId != buyerId)
        {
            throw new EntityNotFoundException("Order not found.");
        }

        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderIdSpecification(orderId), cancellationToken);
        await RefreshNonTerminalAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var existing = await _notifications.FirstOrDefaultAsync(
            new ResendByIdempotencySpecification(notificationId, idempotencyKey),
            cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new EntityNotFoundException("Notification not found.");

        await RefreshIfPossibleAsync(original, cancellationToken);

        if (!original.DidNotReachShopper())
        {
            throw new NotificationOperationException("This notification already reached the shopper.");
        }

        var destinationStillOnFile = await DestinationStillRegisteredAsync(original, cancellationToken);
        if (!destinationStillOnFile)
        {
            throw new NotificationOperationException("The destination number is no longer on file.");
        }

        var body = string.IsNullOrEmpty(original.BodyForDisplay)
            ? $"Your eShopOnWeb order #{original.OrderId} has an update."
            : original.BodyForDisplay!;

        var resend = new OrderNotification(
            original.OrderId,
            original.BuyerId,
            OrderNotificationKind.Resend,
            original.Destination,
            body,
            original.ContactNumberId,
            resendOfNotificationId: original.Id,
            idempotencyKey: idempotencyKey);

        await _notifications.AddAsync(resend, cancellationToken);

        var result = await _sms.SendAsync(original.Destination, body, cancellationToken);
        ApplyResult(resend, result);
        await _notifications.UpdateAsync(resend, cancellationToken);
        _logger.LogInformation(
            "Resent notification {SourceNotificationId} as {NotificationId} with provider status {Status}.",
            original.Id,
            resend.Id,
            resend.ProviderStatus);

        return resend;
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new EntityNotFoundException("Notification not found.");

        if (!string.IsNullOrWhiteSpace(notification.ProviderSid))
        {
            var result = await _sms.RedactBodyAsync(notification.ProviderSid, cancellationToken);
            if (!result.ProviderAccepted)
            {
                throw new TwilioProviderException(
                    "The provider could not dispose of the message content.",
                    httpStatusCode: 502);
            }
        }

        notification.RedactLocalContent();
        await _notifications.UpdateAsync(notification, cancellationToken);
        _logger.LogInformation("Redacted content for notification {NotificationId}.", notification.Id);
    }

    public async Task<NotificationReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (to < from)
        {
            throw new CatalogOrderException("The 'to' timestamp must be on or after 'from'.");
        }

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(45));

        var providerList = await _sms.ListSentFromConfiguredNumberAsync(from, to, budget.Token);
        var providerMessages = providerList.Messages;
        var providerSids = providerMessages
            .Where(m => !string.IsNullOrWhiteSpace(m.Sid))
            .Select(m => m.Sid!)
            .ToList();

        var local = await _notifications.ListAsync(
            new NotificationsForReconciliationSpecification(from, to, providerSids),
            budget.Token);

        var localBySid = local
            .Where(n => !string.IsNullOrWhiteSpace(n.ProviderSid))
            .GroupBy(n => n.ProviderSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();
        var seenSids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var message in providerMessages)
        {
            if (string.IsNullOrWhiteSpace(message.Sid))
            {
                continue;
            }

            seenSids.Add(message.Sid);
            if (localBySid.TryGetValue(message.Sid, out var notification))
            {
                matched.Add(new ReconciliationEntry
                {
                    NotificationId = notification.Id,
                    ProviderSid = message.Sid,
                    ProviderStatus = message.Status,
                    EShopStatus = notification.ProviderStatus,
                    DateSent = message.DateSent,
                    DateCreated = message.DateCreated
                });
            }
            else
            {
                providerOnly.Add(new ReconciliationEntry
                {
                    ProviderSid = message.Sid,
                    ProviderStatus = message.Status,
                    DateSent = message.DateSent,
                    DateCreated = message.DateCreated
                });
            }
        }

        var eShopOnly = local
            .Where(n => string.IsNullOrWhiteSpace(n.ProviderSid) || !seenSids.Contains(n.ProviderSid))
            .Where(n => n.CreatedAt >= from && n.CreatedAt <= to)
            .Select(n => new ReconciliationEntry
            {
                NotificationId = n.Id,
                ProviderSid = n.ProviderSid,
                EShopStatus = n.ProviderStatus,
                DateCreated = n.CreatedAt.ToString("O")
            })
            .ToList();

        return new NotificationReconciliationReport
        {
            From = from,
            To = to,
            Matched = matched,
            ProviderOnly = providerOnly,
            EShopOnly = eShopOnly,
            Truncated = providerList.Truncated
        };
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
        DateTimeOffset? scheduleAt,
        CancellationToken cancellationToken)
    {
        var contact = await LatestContactAsync(order.BuyerId, cancellationToken);
        if (contact is null)
        {
            _logger.LogInformation("Skipping {Kind} notification for order {OrderId}; no contact number on file.", kind, order.Id);
            return;
        }

        var notification = new OrderNotification(
            order.Id,
            order.BuyerId,
            kind,
            contact.CanonicalNumber,
            body,
            contact.Id,
            scheduledSendAt: scheduleAt);

        await _notifications.AddAsync(notification, cancellationToken);

        SmsMessageResult result;
        try
        {
            result = scheduleAt is null
                ? await _sms.SendAsync(contact.CanonicalNumber, body, cancellationToken)
                : await _sms.ScheduleAsync(contact.CanonicalNumber, body, scheduleAt.Value, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            result = new SmsMessageResult { ProviderAccepted = false, OutcomeDetail = "The provider call timed out." };
        }
        catch (Exception)
        {
            result = new SmsMessageResult { ProviderAccepted = false, OutcomeDetail = "The provider call failed." };
        }

        ApplyResult(notification, result);
        await _notifications.UpdateAsync(notification, cancellationToken);
        _logger.LogInformation(
            "Recorded {Kind} notification {NotificationId} for order {OrderId} with provider status {Status}.",
            kind,
            notification.Id,
            order.Id,
            notification.ProviderStatus);
    }

    private static void ApplyResult(OrderNotification notification, SmsMessageResult result)
    {
        if (result.ProviderAccepted)
        {
            notification.ApplyProviderAcceptance(result.Sid, result.Status, result.ErrorCode, result.ErrorMessage);
            return;
        }

        notification.MarkSendFailed(result.OutcomeDetail ?? "The provider did not accept the message.");
        if (!string.IsNullOrWhiteSpace(result.Sid))
        {
            notification.ApplyProviderAcceptance(result.Sid, result.Status ?? OrderNotification.FailedStatus, result.ErrorCode, result.ErrorMessage);
        }
    }

    private async Task<ShopperContactNumber?> LatestContactAsync(string buyerId, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ShopperContactNumbersSpecification(buyerId), cancellationToken);
        return numbers.FirstOrDefault();
    }

    private async Task<bool> DestinationStillRegisteredAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ShopperContactNumbersSpecification(notification.BuyerId), cancellationToken);
        return numbers.Any(n => n.CanonicalNumber == notification.Destination);
    }

    private async Task RefreshNonTerminalAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (notification.IsTerminalStatus() || string.IsNullOrWhiteSpace(notification.ProviderSid))
            {
                continue;
            }

            await RefreshIfPossibleAsync(notification, cancellationToken);
        }
    }

    private async Task RefreshIfPossibleAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(notification.ProviderSid))
        {
            return;
        }

        try
        {
            var result = await _sms.FetchAsync(notification.ProviderSid, cancellationToken);
            if (result.ProviderAccepted)
            {
                notification.RefreshFromProvider(result.Status, result.ErrorCode, result.ErrorMessage);
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
        }
        catch (Exception)
        {
            _logger.LogWarning("Could not refresh provider status for notification {NotificationId}.", notification.Id);
        }
    }
}
