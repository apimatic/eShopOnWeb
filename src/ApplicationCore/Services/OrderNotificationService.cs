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
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates the SMS notification capability. Notifications are best-effort: a message that
/// cannot be sent never fails the underlying order operation, and a shopper with no number on file
/// is simply not messaged. Destination numbers are treated as PII and never written to logs.
/// </summary>
public class OrderNotificationService : IOrderNotificationService
{
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _catalogItemRepository;
    private readonly IOrderNotificationGateway _gateway;
    private readonly IUriComposer _uriComposer;
    private readonly NotificationSettings _settings;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<ContactNumber> contactNumberRepository,
        IRepository<OrderNotification> notificationRepository,
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> catalogItemRepository,
        IOrderNotificationGateway gateway,
        IUriComposer uriComposer,
        NotificationSettings settings,
        IAppLogger<OrderNotificationService> logger)
    {
        _contactNumberRepository = contactNumberRepository;
        _notificationRepository = notificationRepository;
        _orderRepository = orderRepository;
        _catalogItemRepository = catalogItemRepository;
        _gateway = gateway;
        _uriComposer = uriComposer;
        _settings = settings;
        _logger = logger;
    }

    // ============================================================ Flow 1: contact numbers

    public async Task<ContactNumber> RegisterContactNumberAsync(string buyerId, string rawNumber, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrWhiteSpace(rawNumber, nameof(rawNumber));

        // Reject an unusable destination here, at registration, rather than when a later message fails.
        var validation = await _gateway.ValidateDestinationAsync(rawNumber.Trim(), cancellationToken);
        if (!validation.IsValid || string.IsNullOrEmpty(validation.CanonicalNumber))
        {
            var reasons = validation.ValidationErrors.Count > 0
                ? string.Join(", ", validation.ValidationErrors)
                : "the provider does not consider it a usable SMS destination";
            throw new InvalidDestinationNumberException($"The phone number provided cannot be registered: {reasons}.");
        }

        var canonical = validation.CanonicalNumber;

        // Store the provider's canonical form, and avoid duplicate registrations for the same shopper.
        var existing = await _contactNumberRepository.FirstOrDefaultAsync(
            new ContactNumberByValueForBuyerSpecification(buyerId, canonical), cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var contactNumber = new ContactNumber(buyerId, canonical);
        contactNumber = await _contactNumberRepository.AddAsync(contactNumber, cancellationToken);
        _logger.LogInformation("Registered contact number {ContactNumberId} for a shopper.", contactNumber.Id);
        return contactNumber;
    }

    public async Task<IReadOnlyList<ContactNumber>> ListContactNumbersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var numbers = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return numbers;
    }

    public async Task<bool> RemoveContactNumberAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        // Scoped to the owner: another shopper's number is simply "not found".
        var contactNumber = await _contactNumberRepository.FirstOrDefaultAsync(
            new ContactNumberByIdForBuyerSpecification(buyerId, contactNumberId), cancellationToken);
        if (contactNumber is null)
        {
            return false;
        }

        await _contactNumberRepository.DeleteAsync(contactNumber, cancellationToken);
        _logger.LogInformation("Removed contact number {ContactNumberId} for a shopper.", contactNumberId);
        return true;
    }

    // ============================================================ Flow 2: orders and notifications

    public async Task<OrderWithNotifications> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> lines, Address shipToAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(shipToAddress, nameof(shipToAddress));
        if (lines is null || lines.Count == 0)
        {
            throw new NotificationOperationException("An order must contain at least one item.");
        }

        var catalogItemIds = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItemRepository.ListAsync(new CatalogItemsSpecification(catalogItemIds), cancellationToken);
        var catalogById = catalogItems.ToDictionary(c => c.Id);

        var orderItems = new List<OrderItem>();
        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new NotificationOperationException($"Quantity for catalog item {line.CatalogItemId} must be greater than zero.");
            }
            if (!catalogById.TryGetValue(line.CatalogItemId, out var catalogItem))
            {
                throw new NotificationOperationException($"Catalog item {line.CatalogItemId} does not exist.");
            }

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, shipToAddress, orderItems);
        order = await _orderRepository.AddAsync(order, cancellationToken);
        _logger.LogInformation("Placed order {OrderId} with {LineCount} line(s).", order.Id, orderItems.Count);

        var notifications = await RaiseImmediateAsync(order, NotificationKind.OrderPlaced, cancellationToken);
        return new OrderWithNotifications(order, notifications);
    }

    public async Task<IReadOnlyList<OrderNotification>?> DispatchOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        var raised = new List<OrderNotification>();

        // Tell the shopper it is on its way.
        raised.AddRange(await RaiseImmediateAsync(order, NotificationKind.OrderDispatched, cancellationToken));

        // Queue the "how did delivery go?" follow-up WITH THE PROVIDER for a few days later.
        var sendAt = DateTimeOffset.UtcNow.AddDays(Math.Max(1, _settings.FeedbackDelayDays));
        raised.AddRange(await ScheduleFollowUpAsync(order, sendAt, cancellationToken));

        _logger.LogInformation("Dispatched order {OrderId}; raised {Count} notification(s).", orderId, raised.Count);
        return raised;
    }

    public async Task<IReadOnlyList<OrderNotification>?> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        var affected = new List<OrderNotification>();

        // Critical: call off any not-yet-sent feedback follow-up BEFORE it can go out — asking a
        // customer how their delivery went for a cancelled order is exactly the incident to prevent.
        var existing = await _notificationRepository.ListAsync(new OrderNotificationsByOrderSpecification(orderId), cancellationToken);
        foreach (var followUp in existing.Where(n => n.IsCancelableFollowUp))
        {
            try
            {
                var result = await _gateway.CancelScheduledAsync(followUp.ProviderMessageSid!, cancellationToken);
                followUp.RefreshDeliveryOutcome(result.Status, result.ErrorCode, result.ErrorMessage);
                if (!NotificationStatus.IsTerminal(followUp.Status))
                {
                    followUp.MarkCanceled();
                }
            }
            catch (NotificationGatewayException ex)
            {
                // Never fail the cancel operation because the provider could not be reached.
                _logger.LogWarning("Could not cancel scheduled follow-up {NotificationId}: {Reason}", followUp.Id, ex.Message);
            }
            await _notificationRepository.UpdateAsync(followUp, cancellationToken);
            affected.Add(followUp);
        }

        // Tell the shopper the order was cancelled.
        affected.AddRange(await RaiseImmediateAsync(order, NotificationKind.OrderCancelled, cancellationToken));

        _logger.LogInformation("Cancelled order {OrderId}; called off follow-ups and notified the shopper.", orderId);
        return affected;
    }

    public async Task<IReadOnlyList<OrderWithNotifications>> ListOrdersWithNotificationsAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByBuyerSpecification(buyerId), cancellationToken);

        await RefreshDeliveryOutcomesAsync(notifications, cancellationToken);

        var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => (IReadOnlyList<OrderNotification>)g.OrderBy(n => n.CreatedAt).ToList());
        return orders
            .Select(o => new OrderWithNotifications(o, byOrder.TryGetValue(o.Id, out var ns) ? ns : Array.Empty<OrderNotification>()))
            .ToList();
    }

    public async Task<IReadOnlyList<OrderNotification>?> ListOrderNotificationsAsync(string buyerId, int orderId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null || !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            // Ownership: a shopper never sees another's order — indistinguishable from "no such order".
            return null;
        }

        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByOrderSpecification(orderId), cancellationToken);
        await RefreshDeliveryOutcomesAsync(notifications, cancellationToken);
        return notifications;
    }

    // ============================================================ Flow 3: operator actions

    public async Task<OrderNotification?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(idempotencyKey, nameof(idempotencyKey));

        // Idempotency: a repeat under the same key returns the same result without sending again.
        var alreadyDone = await _notificationRepository.FirstOrDefaultAsync(
            new OrderNotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (alreadyDone is not null)
        {
            _logger.LogInformation("Resend under idempotency key reused notification {NotificationId}; no new message sent.", alreadyDone.Id);
            return alreadyDone;
        }

        var source = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (source is null)
        {
            return null;
        }

        if (source.ContentRedacted || string.IsNullOrEmpty(source.Body))
        {
            throw new NotificationOperationException("The message content has been disposed of and can no longer be re-sent.");
        }

        var resend = OrderNotification
            .ForImmediate(source.BuyerId, source.OrderId, source.Kind, source.ToNumber, source.Body)
            .AsResendOf(source, idempotencyKey);

        try
        {
            var result = await _gateway.SendAsync(source.ToNumber, source.Body, cancellationToken);
            resend.RecordProviderAccepted(result.Sid, result.Status, result.ErrorCode, result.ErrorMessage);
        }
        catch (NotificationGatewayException ex)
        {
            resend.RecordDispatchFailure(ex.Message);
        }

        resend = await _notificationRepository.AddAsync(resend, cancellationToken);
        _logger.LogInformation("Re-sent notification {SourceId} as {NotificationId}.", notificationId, resend.Id);
        return resend;
    }

    public async Task<bool> DisposeNotificationContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            return false;
        }

        // Dispose at the provider FIRST so the text is no longer retrievable there — not merely hidden
        // by this application. If the provider disposal fails, surface it rather than reporting success.
        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            await _gateway.DisposeContentAsync(notification.ProviderMessageSid, cancellationToken);
        }

        notification.DisposeContent();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
        _logger.LogInformation("Disposed of content for notification {NotificationId}.", notificationId);
        return true;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            (from, to) = (to, from);
        }

        // Provider's own record for THIS application's configured sender across the range.
        var providerMessages = await _gateway.ListSentByConfiguredSenderAsync(from, to, cancellationToken);
        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid))
            .GroupBy(m => m.Sid)
            .ToDictionary(g => g.Key, g => g.First());

        // What eShop believes it handed to the provider in the range.
        var localNotifications = await _notificationRepository.ListAsync(
            new OrderNotificationsCreatedInRangeSpecification(from, to), cancellationToken);
        var localSids = new HashSet<string>(
            localNotifications.Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid)).Select(n => n.ProviderMessageSid!));

        var matched = new List<ReconciliationMatch>();
        var onlyInEShop = new List<ReconciliationLocalRecord>();
        foreach (var n in localNotifications)
        {
            if (!string.IsNullOrEmpty(n.ProviderMessageSid) && providerBySid.TryGetValue(n.ProviderMessageSid!, out var pm))
            {
                var statusMatches = string.Equals(n.Status, pm.Status, StringComparison.OrdinalIgnoreCase);
                matched.Add(new ReconciliationMatch(n.Id, n.ProviderMessageSid!, n.Kind, n.OrderId, n.Status, pm.Status, statusMatches));
            }
            else
            {
                onlyInEShop.Add(new ReconciliationLocalRecord(n.Id, n.ProviderMessageSid, n.Kind, n.OrderId, n.Status, n.CreatedAt));
            }
        }

        var onlyAtProvider = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid) && !localSids.Contains(m.Sid))
            .ToList();

        _logger.LogInformation(
            "Reconciliation {From}..{To}: {Matched} matched, {ProviderOnly} provider-only, {EShopOnly} eShop-only.",
            from.ToString("o", CultureInfo.InvariantCulture), to.ToString("o", CultureInfo.InvariantCulture),
            matched.Count, onlyAtProvider.Count, onlyInEShop.Count);

        return new ReconciliationReport(from, to, matched, onlyAtProvider, onlyInEShop);
    }

    // ============================================================ helpers

    /// <summary>Send an immediate notification of <paramref name="kind"/> to each of the order owner's registered numbers.</summary>
    private async Task<IReadOnlyList<OrderNotification>> RaiseImmediateAsync(Order order, NotificationKind kind, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(order.BuyerId), cancellationToken);
        if (numbers.Count == 0)
        {
            return Array.Empty<OrderNotification>(); // no number on file → simply not messaged
        }

        var body = ComposeBody(kind, order.Id);
        var raised = new List<OrderNotification>(numbers.Count);
        foreach (var number in numbers)
        {
            var notification = OrderNotification.ForImmediate(order.BuyerId, order.Id, kind, number.PhoneNumber, body);
            try
            {
                var result = await _gateway.SendAsync(number.PhoneNumber, body, cancellationToken);
                notification.RecordProviderAccepted(result.Sid, result.Status, result.ErrorCode, result.ErrorMessage);
            }
            catch (NotificationGatewayException ex)
            {
                notification.RecordDispatchFailure(ex.Message);
            }
            notification = await _notificationRepository.AddAsync(notification, cancellationToken);
            raised.Add(notification);
        }
        return raised;
    }

    /// <summary>Queue a delivery-feedback follow-up with the provider for each of the order owner's registered numbers.</summary>
    private async Task<IReadOnlyList<OrderNotification>> ScheduleFollowUpAsync(Order order, DateTimeOffset sendAt, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(order.BuyerId), cancellationToken);
        if (numbers.Count == 0)
        {
            return Array.Empty<OrderNotification>();
        }

        var body = ComposeBody(NotificationKind.DeliveryFeedback, order.Id);
        var raised = new List<OrderNotification>(numbers.Count);
        foreach (var number in numbers)
        {
            var notification = OrderNotification.ForScheduled(order.BuyerId, order.Id, NotificationKind.DeliveryFeedback, number.PhoneNumber, body, sendAt);
            try
            {
                var result = await _gateway.ScheduleAsync(number.PhoneNumber, body, sendAt, cancellationToken);
                notification.RecordProviderAccepted(result.Sid, result.Status, result.ErrorCode, result.ErrorMessage);
            }
            catch (NotificationGatewayException ex)
            {
                notification.RecordDispatchFailure(ex.Message);
            }
            notification = await _notificationRepository.AddAsync(notification, cancellationToken);
            raised.Add(notification);
        }
        return raised;
    }

    /// <summary>Bring each non-terminal notification's delivery outcome up to date from the provider.</summary>
    private async Task RefreshDeliveryOutcomesAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var n in notifications)
        {
            if (string.IsNullOrEmpty(n.ProviderMessageSid) || NotificationStatus.IsTerminal(n.Status))
            {
                continue;
            }
            try
            {
                var pm = await _gateway.FetchAsync(n.ProviderMessageSid!, cancellationToken);
                n.RefreshDeliveryOutcome(pm.Status, pm.ErrorCode, pm.ErrorMessage);
                await _notificationRepository.UpdateAsync(n, cancellationToken);
            }
            catch (NotificationGatewayException ex)
            {
                _logger.LogWarning("Could not refresh delivery outcome for notification {NotificationId}: {Reason}", n.Id, ex.Message);
            }
        }
    }

    private static string ComposeBody(NotificationKind kind, int orderId) => kind switch
    {
        NotificationKind.OrderPlaced => $"eShop: your order #{orderId} has been placed. Thank you for shopping with us!",
        NotificationKind.OrderDispatched => $"eShop: good news — your order #{orderId} is on its way!",
        NotificationKind.DeliveryFeedback => $"eShop: how did the delivery of your order #{orderId} go? We'd love your feedback.",
        NotificationKind.OrderCancelled => $"eShop: your order #{orderId} has been cancelled. If this is unexpected, please contact support.",
        _ => $"eShop: an update about your order #{orderId}."
    };
}
