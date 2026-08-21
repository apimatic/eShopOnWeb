using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    public static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private static readonly Address DefaultShipTo =
        new("123 Main St.", "Kent", "OH", "United States", "44240");

    private readonly IOrderService _orderService;
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IShopperContactService _contactService;
    private readonly ISmsGateway _smsGateway;
    private readonly TwilioSettings _twilioSettings;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IOrderService orderService,
        IRepository<Order> orderRepository,
        IRepository<OrderNotification> notificationRepository,
        IShopperContactService contactService,
        ISmsGateway smsGateway,
        TwilioSettings twilioSettings,
        IAppLogger<OrderNotificationService> logger)
    {
        _orderService = orderService;
        _orderRepository = orderRepository;
        _notificationRepository = notificationRepository;
        _contactService = contactService;
        _smsGateway = smsGateway;
        _twilioSettings = twilioSettings;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<CatalogQuantity> items,
        Address shippingAddress,
        CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var address = shippingAddress ?? DefaultShipTo;
        var order = await _orderService.CreateOrderFromCatalogItemsAsync(buyerId, items, address);

        await TryNotifyAsync(
            order,
            NotificationKind.OrderPlaced,
            $"Your eShopOnWeb order #{order.Id} has been placed.",
            sendAt: null,
            cancellationToken);

        return order;
    }

    public async Task DispatchAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken)
            ?? throw new OrderLifecycleException("Order not found.");

        try
        {
            order.MarkDispatched();
        }
        catch (InvalidOperationException ex)
        {
            throw new OrderLifecycleException(ex.Message);
        }

        await _orderRepository.UpdateAsync(order, cancellationToken);

        await TryNotifyAsync(
            order,
            NotificationKind.OrderDispatched,
            $"Your eShopOnWeb order #{order.Id} is on its way.",
            sendAt: null,
            cancellationToken);

        await TryNotifyAsync(
            order,
            NotificationKind.DeliveryFollowUp,
            $"How did your eShopOnWeb delivery go for order #{order.Id}?",
            sendAt: DateTimeOffset.UtcNow.Add(FollowUpDelay),
            cancellationToken);
    }

    public async Task CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken)
            ?? throw new OrderLifecycleException("Order not found.");

        var followUps = await _notificationRepository.ListAsync(
            new ScheduledFollowUpsByOrderSpec(orderId), cancellationToken);
        foreach (var followUp in followUps)
        {
            try
            {
                var cancelled = await _smsGateway.CancelScheduledAsync(followUp.ProviderSid, cancellationToken);
                if (cancelled)
                {
                    followUp.ApplyProviderOutcome("canceled", null, null, followUp.Body);
                    await _notificationRepository.UpdateAsync(followUp, cancellationToken);
                }
                else
                {
                    await RefreshOneAsync(followUp, cancellationToken);
                }
            }
            catch (SmsProviderException)
            {
                _logger.LogWarning("Failed to cancel scheduled follow-up {NotificationId} for order {OrderId}", followUp.Id, orderId);
            }
        }

        try
        {
            order.MarkCancelled();
        }
        catch (InvalidOperationException ex)
        {
            throw new OrderLifecycleException(ex.Message);
        }

        await _orderRepository.UpdateAsync(order, cancellationToken);

        await TryNotifyAsync(
            order,
            NotificationKind.OrderCancelled,
            $"Your eShopOnWeb order #{order.Id} has been cancelled.",
            sendAt: null,
            cancellationToken);
    }

    public async Task<IReadOnlyList<OrderWithNotifications>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        if (orders.Count == 0)
        {
            return Array.Empty<OrderWithNotifications>();
        }

        var notifications = await _notificationRepository.ListAsync(
            new NotificationsByOrderIdsSpec(orders.Select(o => o.Id)), cancellationToken);
        var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => (IReadOnlyList<OrderNotification>)g.ToList());

        return orders.Select(order => new OrderWithNotifications
        {
            Order = order,
            Notifications = byOrder.TryGetValue(order.Id, out var list) ? list : Array.Empty<OrderNotification>()
        }).ToList();
    }

    public async Task<IReadOnlyList<OrderNotification>> GetNotificationsAsync(
        int orderId,
        string buyerId,
        bool isAdministrator,
        CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null || (!isAdministrator && !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal)))
        {
            throw new OrderLifecycleException("Order not found.");
        }

        var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderSpec(orderId), cancellationToken);
        foreach (var notification in notifications)
        {
            await RefreshOneAsync(notification, cancellationToken);
        }

        return notifications;
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var original = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new OrderLifecycleException("Notification not found.");

        var existing = await _notificationRepository.FirstOrDefaultAsync(
            new ResendByIdempotencySpec(notificationId, idempotencyKey), cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        if (original.ContentRedacted || string.IsNullOrEmpty(original.Body))
        {
            throw new OrderLifecycleException("The original message content is no longer available to resend.");
        }

        var to = await _contactService.GetPrimaryNumberAsync(original.BuyerId, cancellationToken);
        if (string.IsNullOrEmpty(to))
        {
            throw new OrderLifecycleException("The shopper has no contact number on file.");
        }

        SmsDispatchResult? sent;
        try
        {
            sent = await _smsGateway.SendAsync(to, original.Body, cancellationToken);
        }
        catch (SmsProviderException)
        {
            _logger.LogWarning("Resend failed at the provider for notification {NotificationId}", notificationId);
            throw new OrderLifecycleException("The messaging provider could not send the message.");
        }

        if (sent is null || string.IsNullOrEmpty(sent.ProviderSid))
        {
            throw new OrderLifecycleException("The messaging provider could not send the message.");
        }

        var resend = new OrderNotification(
            original.OrderId,
            original.BuyerId,
            sent.ProviderSid,
            NotificationKind.Resend,
            sent.Status,
            original.Body,
            sendAt: null,
            resendOfNotificationId: original.Id,
            idempotencyKey: idempotencyKey);
        resend.ApplyProviderOutcome(sent.Status, sent.ErrorCode, sent.ErrorMessage, sent.Body);
        return await _notificationRepository.AddAsync(resend, cancellationToken);
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new OrderLifecycleException("Notification not found.");

        if (notification.ContentRedacted)
        {
            return;
        }

        var originalBody = notification.Body ?? string.Empty;
        try
        {
            var redacted = await _smsGateway.RedactBodyAsync(notification.ProviderSid, originalBody, cancellationToken);
            if (!redacted)
            {
                throw new OrderLifecycleException("The provider still returns the original message text.");
            }
        }
        catch (SmsProviderException)
        {
            throw new OrderLifecycleException("The provider could not dispose of the message content.");
        }

        notification.MarkContentRedacted();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        IReadOnlyList<ProviderMessageRecord> providerMessages;
        bool truncated;
        try
        {
            var listed = await _smsGateway.ListSentFromConfiguredNumberAsync(from, to, cancellationToken);
            providerMessages = listed.Messages;
            truncated = listed.Truncated;
        }
        catch (SmsProviderException)
        {
            throw;
        }

        var providerSids = providerMessages
            .Select(m => m.ProviderSid)
            .Where(s => !string.IsNullOrEmpty(s))
            .Distinct()
            .ToArray();

        var eShopInRange = await _notificationRepository.ListAsync(
            new NotificationsCreatedInRangeSpec(from, to), cancellationToken);
        var eShopBySid = eShopInRange
            .Where(n => !string.IsNullOrEmpty(n.ProviderSid))
            .GroupBy(n => n.ProviderSid)
            .ToDictionary(g => g.Key, g => g.First());

        var providerMatchedEShop = new Dictionary<string, OrderNotification>();
        if (providerSids.Length > 0)
        {
            var eShopForProviderSids = await _notificationRepository.ListAsync(
                new NotificationsByProviderSidsSpec(providerSids), cancellationToken);
            foreach (var n in eShopForProviderSids)
            {
                providerMatchedEShop[n.ProviderSid] = n;
            }
        }

        var matched = new List<ReconciliationRow>();
        var providerOnly = new List<ReconciliationRow>();
        foreach (var provider in providerMessages)
        {
            if (providerMatchedEShop.TryGetValue(provider.ProviderSid, out var local))
            {
                matched.Add(new ReconciliationRow
                {
                    NotificationId = local.Id,
                    ProviderSid = provider.ProviderSid,
                    ProviderStatus = provider.Status,
                    EShopStatus = local.ProviderStatus,
                    OrderId = local.OrderId
                });
            }
            else
            {
                providerOnly.Add(new ReconciliationRow
                {
                    ProviderSid = provider.ProviderSid,
                    ProviderStatus = provider.Status
                });
            }
        }

        var providerSidSet = new HashSet<string>(providerSids, StringComparer.Ordinal);
        var eShopOnly = eShopInRange
            .Where(n => !providerSidSet.Contains(n.ProviderSid))
            .Select(n => new ReconciliationRow
            {
                NotificationId = n.Id,
                ProviderSid = n.ProviderSid,
                EShopStatus = n.ProviderStatus,
                OrderId = n.OrderId
            })
            .ToList();

        return new ReconciliationReport
        {
            From = from,
            To = to,
            FromNumber = _twilioSettings.FromNumber,
            Truncated = truncated,
            Matched = matched,
            ProviderOnly = providerOnly,
            EShopOnly = eShopOnly
        };
    }

    private async Task TryNotifyAsync(
        Order order,
        NotificationKind kind,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        string? to;
        try
        {
            to = await _contactService.GetPrimaryNumberAsync(order.BuyerId, cancellationToken);
        }
        catch (Exception)
        {
            _logger.LogWarning("Could not load a contact number while notifying for order {OrderId}", order.Id);
            return;
        }

        if (string.IsNullOrEmpty(to))
        {
            return;
        }

        try
        {
            SmsDispatchResult? result = sendAt.HasValue
                ? await _smsGateway.ScheduleAsync(to, body, sendAt.Value, cancellationToken)
                : await _smsGateway.SendAsync(to, body, cancellationToken);

            if (result is null || string.IsNullOrEmpty(result.ProviderSid))
            {
                _logger.LogWarning("Provider accepted no message identifier for order {OrderId} kind {Kind}", order.Id, kind);
                return;
            }

            var notification = new OrderNotification(
                order.Id,
                order.BuyerId,
                result.ProviderSid,
                kind,
                result.Status,
                body,
                sendAt);
            notification.ApplyProviderOutcome(result.Status, result.ErrorCode, result.ErrorMessage, result.Body);
            await _notificationRepository.AddAsync(notification, cancellationToken);
        }
        catch (SmsProviderException)
        {
            _logger.LogWarning("Provider send failed for order {OrderId} kind {Kind}; the order operation continues.", order.Id, kind);
        }
        catch (Exception)
        {
            _logger.LogWarning("Unexpected failure while notifying for order {OrderId} kind {Kind}; the order operation continues.", order.Id, kind);
        }
    }

    private async Task RefreshOneAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            var fresh = await _smsGateway.FetchAsync(notification.ProviderSid, cancellationToken);
            if (fresh is null)
            {
                return;
            }

            notification.ApplyProviderOutcome(fresh.Status, fresh.ErrorCode, fresh.ErrorMessage, notification.ContentRedacted ? null : fresh.Body);
            await _notificationRepository.UpdateAsync(notification, cancellationToken);
        }
        catch (SmsProviderException)
        {
            _logger.LogWarning("Could not refresh provider status for notification {NotificationId}", notification.Id);
        }
    }
}
