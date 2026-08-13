using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    /// <summary>How far ahead the "how did the delivery go?" follow-up is queued with the provider.</summary>
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<Notification> _notificationRepository;
    private readonly IReadRepository<ContactNumber> _contactNumberRepository;
    private readonly ISmsProvider _smsProvider;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<Notification> notificationRepository,
        IReadRepository<ContactNumber> contactNumberRepository,
        ISmsProvider smsProvider,
        IUriComposer uriComposer,
        IAppLogger<OrderNotificationService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _notificationRepository = notificationRepository;
        _contactNumberRepository = contactNumberRepository;
        _smsProvider = smsProvider;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, Address shipToAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(shipToAddress, nameof(shipToAddress));

        if (lines is null || lines.Count == 0)
        {
            throw new OrderCreationException("An order must contain at least one item.");
        }

        if (lines.Any(l => l.Quantity <= 0))
        {
            throw new OrderCreationException("Every order line must have a quantity of at least one.");
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        var byId = catalogItems.ToDictionary(c => c.Id);

        var missing = ids.Where(id => !byId.ContainsKey(id)).ToArray();
        if (missing.Length > 0)
        {
            throw new OrderCreationException($"Unknown catalog item(s): {string.Join(", ", missing)}.");
        }

        var items = lines.Select(line =>
        {
            var catalogItem = byId[line.CatalogItemId];
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, items);
        await _orderRepository.AddAsync(order, cancellationToken);
        _logger.LogInformation("Placed order {OrderId} for buyer {BuyerId}.", order.Id, buyerId);

        await NotifyAsync(order, NotificationKind.OrderPlaced, BuildPlacedBody(order), cancellationToken);

        return order;
    }

    public async Task<Order?> DispatchOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        order.Dispatch();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Dispatched order {OrderId}.", order.Id);

        await NotifyAsync(order, NotificationKind.OrderDispatched, BuildDispatchedBody(order), cancellationToken);
        await ScheduleFollowUpAsync(order, cancellationToken);

        return order;
    }

    public async Task<Order?> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        order.Cancel();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Cancelled order {OrderId}.", order.Id);

        // Call off any follow-up that has not yet gone out BEFORE telling the shopper, so a "how did
        // delivery go?" message can never reach a customer whose order was cancelled.
        await CancelPendingFollowUpsAsync(order, cancellationToken);
        await NotifyAsync(order, NotificationKind.OrderCancelled, BuildCancelledBody(order), cancellationToken);

        return order;
    }

    public async Task<Notification?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        // Idempotency: a repeat under the same key returns the message the first attempt produced,
        // without sending again. A genuine second attempt uses a fresh key.
        var alreadyProduced = await _notificationRepository.FirstOrDefaultAsync(
            new NotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (alreadyProduced is not null)
        {
            _logger.LogInformation("Resend under idempotency key already satisfied by notification {NotificationId}; no message sent.", alreadyProduced.Id);
            return alreadyProduced;
        }

        var source = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (source is null)
        {
            return null;
        }

        if (string.IsNullOrEmpty(source.Body))
        {
            throw new NotificationContentUnavailableException(notificationId);
        }

        // Claim the idempotency key by persisting the new record before sending, so a concurrent repeat
        // under the same key cannot produce a second message.
        var resend = new Notification(source.OrderId, source.OwnerId, NotificationKind.Resend, source.ToNumber, source.Body);
        resend.SetIdempotencyKey(idempotencyKey);
        await _notificationRepository.AddAsync(resend, cancellationToken);

        try
        {
            var message = await _smsProvider.SendAsync(source.ToNumber, source.Body, cancellationToken);
            resend.RecordProviderResult(message.Sid, message.Status, message.ErrorCode, message.ErrorMessage, message.DateSent);
            _logger.LogInformation("Resent notification {SourceId} as {ResendId} (provider status {Status}).", notificationId, resend.Id, message.Status);
        }
        catch (Exception ex)
        {
            resend.RecordSendFailed(ex.Message);
            _logger.LogWarning("Resend of notification {SourceId} failed to reach the provider: {Error}", notificationId, ex.Message);
        }

        await _notificationRepository.UpdateAsync(resend, cancellationToken);
        return resend;
    }

    public async Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            return false;
        }

        // Redact at the provider first. This is a compliance action, not a best-effort notification:
        // if the provider copy cannot be removed we must not report success, so failures propagate.
        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            await _smsProvider.RedactContentAsync(notification.ProviderMessageSid, cancellationToken);
        }

        notification.DisposeContent();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
        _logger.LogInformation("Disposed of content for notification {NotificationId}.", notificationId);
        return true;
    }

    public async Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Ask the provider only for this application's own sending number's messages over the range.
        var providerMessages = await _smsProvider.ListMessagesFromConfiguredNumberAsync(from, to, cancellationToken);

        // Everything we have a provider identifier for, keyed by that identifier.
        var localWithSid = await _notificationRepository.ListAsync(new NotificationsWithProviderSidUpToSpecification(to), cancellationToken);
        var localBySid = localWithSid
            .Where(n => n.ProviderMessageSid is not null)
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());
        var providerSids = new HashSet<string>(providerMessages.Select(m => m.Sid));

        var matched = new List<ReconciledMessage>();
        var providerOnly = new List<ProviderOnlyMessage>();
        foreach (var message in providerMessages)
        {
            if (localBySid.TryGetValue(message.Sid, out var local))
            {
                matched.Add(new ReconciledMessage(local.Id, message.Sid, local.DeliveryStatus, message.Status));
            }
            else
            {
                providerOnly.Add(new ProviderOnlyMessage(message.Sid, message.Status, message.DateSent));
            }
        }

        // Messages we believe we sent within the range that the provider did not return.
        var localSentInRange = await _notificationRepository.ListAsync(new NotificationsSentInRangeSpecification(from, to), cancellationToken);
        var eShopOnly = localSentInRange
            .Where(n => n.ProviderMessageSid is not null && !providerSids.Contains(n.ProviderMessageSid!))
            .Select(n => new EShopOnlyMessage(n.Id, n.ProviderMessageSid!, n.DeliveryStatus))
            .ToList();

        _logger.LogInformation("Reconciliation {From:o}..{To:o}: provider={ProviderCount}, matched={Matched}, providerOnly={ProviderOnly}, eShopOnly={EShopOnly}.",
            from, to, providerMessages.Count, matched.Count, providerOnly.Count, eShopOnly.Count);

        return new NotificationReconciliationReport(from, to, matched, providerOnly, eShopOnly);
    }

    public async Task<IReadOnlyList<Notification>> GetOrderNotificationsAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderSpecification(orderId), cancellationToken);

        // Lazily refresh non-terminal messages so each carries the provider's current delivery outcome.
        // There is no callback URL into this app, so the state must be pulled from the provider on read.
        foreach (var notification in notifications)
        {
            if (notification.ProviderMessageSid is null || IsTerminal(notification.DeliveryStatus))
            {
                continue;
            }

            try
            {
                var message = await _smsProvider.FetchAsync(notification.ProviderMessageSid, cancellationToken);
                if (message.Status != notification.DeliveryStatus || message.DateSent is not null || message.ErrorCode is not null)
                {
                    notification.UpdateDeliveryState(message.Status, message.ErrorCode, message.ErrorMessage, message.DateSent);
                    await _notificationRepository.UpdateAsync(notification, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not refresh delivery status for notification {NotificationId}: {Error}", notification.Id, ex.Message);
            }
        }

        return notifications;
    }

    private static bool IsTerminal(string status) => status switch
    {
        "delivered" or "undelivered" or "failed" or "canceled" or Notification.StatusSendFailed => true,
        _ => false
    };

    /// <summary>
    /// Sends a message to the shopper's current number for an order and records it. A shopper with no
    /// number on file is simply not messaged; a send that fails never propagates to the caller.
    /// </summary>
    private async Task NotifyAsync(Order order, NotificationKind kind, string body, CancellationToken cancellationToken)
    {
        var toNumber = await ResolveShopperNumberAsync(order.BuyerId, cancellationToken);
        if (toNumber is null)
        {
            _logger.LogInformation("No contact number on file for order {OrderId}; no {Kind} message sent.", order.Id, kind);
            return;
        }

        var notification = new Notification(order.Id, order.BuyerId, kind, toNumber, body);
        await _notificationRepository.AddAsync(notification, cancellationToken);

        try
        {
            var message = await _smsProvider.SendAsync(toNumber, body, cancellationToken);
            notification.RecordProviderResult(message.Sid, message.Status, message.ErrorCode, message.ErrorMessage, message.DateSent);
            _logger.LogInformation("Sent {Kind} message for order {OrderId} as notification {NotificationId} (provider status {Status}).", kind, order.Id, notification.Id, message.Status);
        }
        catch (Exception ex)
        {
            notification.RecordSendFailed(ex.Message);
            _logger.LogWarning("{Kind} message for order {OrderId} could not be sent: {Error}", kind, order.Id, ex.Message);
        }

        await _notificationRepository.UpdateAsync(notification, cancellationToken);
    }

    /// <summary>Queues the delivery follow-up with the provider for a few days out. Never propagates failures.</summary>
    private async Task ScheduleFollowUpAsync(Order order, CancellationToken cancellationToken)
    {
        var toNumber = await ResolveShopperNumberAsync(order.BuyerId, cancellationToken);
        if (toNumber is null)
        {
            return;
        }

        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        var body = BuildFollowUpBody(order);
        var notification = new Notification(order.Id, order.BuyerId, NotificationKind.DeliveryFollowUp, toNumber, body);
        await _notificationRepository.AddAsync(notification, cancellationToken);

        try
        {
            var message = await _smsProvider.ScheduleAsync(toNumber, body, sendAt, cancellationToken);
            notification.RecordScheduled(message.Sid, message.Status, sendAt);
            _logger.LogInformation("Scheduled delivery follow-up for order {OrderId} as notification {NotificationId} (due {SendAt:o}, provider status {Status}).", order.Id, notification.Id, sendAt, message.Status);
        }
        catch (Exception ex)
        {
            notification.RecordSendFailed(ex.Message);
            _logger.LogWarning("Delivery follow-up for order {OrderId} could not be scheduled: {Error}", order.Id, ex.Message);
        }

        await _notificationRepository.UpdateAsync(notification, cancellationToken);
    }

    /// <summary>Cancels any still-scheduled follow-up for the order at the provider. Never propagates failures.</summary>
    private async Task CancelPendingFollowUpsAsync(Order order, CancellationToken cancellationToken)
    {
        var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderSpecification(order.Id), cancellationToken);
        var pendingFollowUps = notifications
            .Where(n => n.Kind == NotificationKind.DeliveryFollowUp
                && n.ProviderMessageSid is not null
                && n.DeliveryStatus == "scheduled");

        foreach (var followUp in pendingFollowUps)
        {
            try
            {
                var message = await _smsProvider.CancelScheduledAsync(followUp.ProviderMessageSid!, cancellationToken);
                followUp.UpdateDeliveryState(message.Status, message.ErrorCode, message.ErrorMessage, message.DateSent);
                _logger.LogInformation("Called off scheduled follow-up {NotificationId} for cancelled order {OrderId} (provider status {Status}).", followUp.Id, order.Id, message.Status);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not cancel scheduled follow-up {NotificationId} for order {OrderId}: {Error}", followUp.Id, order.Id, ex.Message);
            }

            await _notificationRepository.UpdateAsync(followUp, cancellationToken);
        }
    }

    private async Task<string?> ResolveShopperNumberAsync(string buyerId, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumberRepository.ListAsync(new ContactNumbersByOwnerSpecification(buyerId), cancellationToken);
        // Newest-first ordering from the specification: message the shopper's current number.
        return numbers.FirstOrDefault()?.PhoneNumber;
    }

    private static string Money(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static string BuildPlacedBody(Order order) =>
        $"eShop: your order #{order.Id} has been placed (total {Money(order.Total())}). Thank you for shopping with us!";

    private static string BuildDispatchedBody(Order order) =>
        $"eShop: good news - your order #{order.Id} is on its way!";

    private static string BuildFollowUpBody(Order order) =>
        $"eShop: how did the delivery of your order #{order.Id} go? We'd love your feedback.";

    private static string BuildCancelledBody(Order order) =>
        $"eShop: your order #{order.Id} has been cancelled. If this was not expected, please contact support.";
}
