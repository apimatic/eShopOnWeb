using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SmsNotificationService : ISmsNotificationService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly ISmsGateway _gateway;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<SmsNotificationService> _logger;
    private readonly NotificationSettings _settings;

    public SmsNotificationService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<ContactNumber> contactNumberRepository,
        IRepository<OrderNotification> notificationRepository,
        ISmsGateway gateway,
        IUriComposer uriComposer,
        IAppLogger<SmsNotificationService> logger,
        NotificationSettings settings)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _contactNumberRepository = contactNumberRepository;
        _notificationRepository = notificationRepository;
        _gateway = gateway;
        _uriComposer = uriComposer;
        _logger = logger;
        _settings = settings;
    }

    // ---------------- Flow 1: contact numbers ----------------

    public async Task<RegisterNumberResult> RegisterContactNumberAsync(string buyerId, string rawNumber, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        if (string.IsNullOrWhiteSpace(rawNumber))
            return new RegisterNumberResult(null, "A phone number is required.");

        // Reject an unusable destination here, at registration, rather than when a message later fails.
        var validation = await _gateway.ValidateNumberAsync(rawNumber, ct);
        if (!validation.IsValid || string.IsNullOrEmpty(validation.CanonicalNumber))
        {
            return new RegisterNumberResult(null,
                validation.Reason ?? "The number is not a usable SMS destination.");
        }

        var canonical = validation.CanonicalNumber;

        // Store the provider's canonical form; if the shopper already has it, return the existing record.
        var existing = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), ct);
        var already = existing.FirstOrDefault(cn => cn.CanonicalNumber == canonical);
        if (already is not null)
            return new RegisterNumberResult(already, null);

        var contactNumber = new ContactNumber(buyerId, canonical);
        await _contactNumberRepository.AddAsync(contactNumber, ct);
        _logger.LogInformation("Registered contact number {ContactNumberId} for a shopper.", contactNumber.Id);
        return new RegisterNumberResult(contactNumber, null);
    }

    public async Task<IReadOnlyList<ContactNumber>> GetContactNumbersAsync(string buyerId, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), ct);
    }

    public async Task<bool> DeleteContactNumberAsync(string buyerId, int contactNumberId, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var contactNumber = await _contactNumberRepository.GetByIdAsync(contactNumberId, ct);
        // One shopper must never delete another's — an unowned or missing number is simply "not found".
        if (contactNumber is null || contactNumber.BuyerId != buyerId)
            return false;

        await _contactNumberRepository.DeleteAsync(contactNumber, ct);
        _logger.LogInformation("Removed contact number {ContactNumberId} for a shopper.", contactNumberId);
        return true;
    }

    // ---------------- Flow 2: order transitions ----------------

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> lines, Address shipToAddress, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(shipToAddress, nameof(shipToAddress));
        if (lines is null || lines.Count == 0)
            throw new ArgumentException("An order must contain at least one item.", nameof(lines));
        if (lines.Any(l => l.Units < 1))
            throw new ArgumentException("Every order line must have a quantity of at least one.", nameof(lines));

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), ct);
        var missing = ids.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
            throw new ArgumentException($"Unknown catalog item id(s): {string.Join(", ", missing)}.", nameof(lines));

        var items = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Units);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, items);
        await _orderRepository.AddAsync(order, ct);
        _logger.LogInformation("Placed order {OrderId} for a shopper.", order.Id);

        await NotifyAsync(order, NotificationKind.OrderPlaced, ct);
        return order;
    }

    public async Task<OrderTransitionResult> DispatchOrderAsync(int orderId, CancellationToken ct = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, ct);
        if (order is null)
            return OrderTransitionResult.OrderNotFound;

        var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderSpecification(orderId), ct);
        if (notifications.Any(n => n.Kind == NotificationKind.OrderDispatched))
            return OrderTransitionResult.AlreadyInState;

        // Tell the shopper it is on its way, then queue the delivery follow-up with the provider for later.
        await NotifyAsync(order, NotificationKind.OrderDispatched, ct);
        await ScheduleFollowUpAsync(order, ct);
        _logger.LogInformation("Dispatched order {OrderId}.", orderId);
        return OrderTransitionResult.Success;
    }

    public async Task<OrderTransitionResult> CancelOrderAsync(int orderId, CancellationToken ct = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, ct);
        if (order is null)
            return OrderTransitionResult.OrderNotFound;

        var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderSpecification(orderId), ct);
        if (notifications.Any(n => n.Kind == NotificationKind.OrderCanceled))
            return OrderTransitionResult.AlreadyInState;

        // A follow-up that has not yet gone out must never reach the shopper: call every scheduled one off first.
        var scheduled = await _notificationRepository.ListAsync(new ScheduledFollowUpsByOrderSpecification(orderId), ct);
        foreach (var followUp in scheduled)
        {
            var canceled = await _gateway.CancelScheduledAsync(followUp.ProviderMessageSid!, ct);
            if (canceled)
            {
                followUp.MarkScheduleCanceled();
                await _notificationRepository.UpdateAsync(followUp, ct);
                _logger.LogInformation("Called off scheduled follow-up {NotificationId} for cancelled order {OrderId}.", followUp.Id, orderId);
            }
            else
            {
                _logger.LogWarning("Could not call off scheduled follow-up {NotificationId} for cancelled order {OrderId}.", followUp.Id, orderId);
            }
        }

        await NotifyAsync(order, NotificationKind.OrderCanceled, ct);
        _logger.LogInformation("Cancelled order {OrderId}.", orderId);
        return OrderTransitionResult.Success;
    }

    public async Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);
    }

    public Task<Order?> GetOrderAsync(int orderId, CancellationToken ct = default) =>
        _orderRepository.GetByIdAsync(orderId, ct);

    public async Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(int orderId, CancellationToken ct = default)
    {
        var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderSpecification(orderId), ct);
        await RefreshDeliveryOutcomesAsync(notifications, ct);
        return notifications;
    }

    // ---------------- Flow 3: operator actions ----------------

    public async Task<ResendOutcome> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        // Repeating a request under the same key must not send a second message.
        var replay = await _notificationRepository.FirstOrDefaultAsync(new NotificationByIdempotencyKeySpecification(idempotencyKey), ct);
        if (replay is not null)
            return new ResendOutcome(ResendResultCode.ReplayedIdempotent, replay.Id, replay.DeliveryStatus);

        var original = await _notificationRepository.GetByIdAsync(notificationId, ct);
        if (original is null)
            return new ResendOutcome(ResendResultCode.NotificationNotFound, null, null);

        if (original.ContentDisposed || string.IsNullOrEmpty(original.Body))
            return new ResendOutcome(ResendResultCode.ContentDisposed, null, null);

        // Nothing may be sent to a number that has been removed.
        if (original.ContactNumberId is null)
            return new ResendOutcome(ResendResultCode.NumberRemoved, null, null);
        var target = await _contactNumberRepository.GetByIdAsync(original.ContactNumberId.Value, ct);
        if (target is null)
            return new ResendOutcome(ResendResultCode.NumberRemoved, null, null);

        var resend = new OrderNotification(original.OrderId, original.BuyerId, original.ToNumber,
            original.ContactNumberId, original.Kind, original.Body);
        resend.SetResendMetadata(original.Id, idempotencyKey);

        var result = await _gateway.SendAsync(original.ToNumber, original.Body, ct);
        if (result.Accepted && !string.IsNullOrEmpty(result.ProviderMessageSid))
            resend.MarkSentToProvider(result.ProviderMessageSid!, result.Status);
        else
            resend.MarkSendFailed(result.ErrorCode, result.ErrorMessage);

        await _notificationRepository.AddAsync(resend, ct);
        _logger.LogInformation("Re-sent notification {OriginalId} as {NotificationId} (status {Status}).",
            original.Id, resend.Id, resend.DeliveryStatus);
        return new ResendOutcome(ResendResultCode.Resent, resend.Id, resend.DeliveryStatus);
    }

    public async Task<DisposeResultCode> DisposeContentAsync(int notificationId, CancellationToken ct = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, ct);
        if (notification is null)
            return DisposeResultCode.NotFound;

        if (notification.ContentDisposed)
            return DisposeResultCode.Disposed; // already gone; idempotent

        // Redact at the provider first so the text is no longer retrievable there either, then clear it here.
        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
            await _gateway.RedactBodyAsync(notification.ProviderMessageSid!, ct);

        notification.DisposeContent();
        await _notificationRepository.UpdateAsync(notification, ct);
        _logger.LogInformation("Disposed content of notification {NotificationId}; record and outcome retained.", notificationId);
        return DisposeResultCode.Disposed;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        // Ask the provider only for this application's own sending number's messages over the whole range.
        var providerMessages = await _gateway.ListSentMessagesAsync(from, to, ct);
        var eShopNotifications = await _notificationRepository.ListAsync(new NotificationsWithProviderIdInRangeSpecification(from, to), ct);

        var eShopBySid = eShopNotifications
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());
        var providerBySid = providerMessages
            .GroupBy(m => m.Sid)
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();
        var eShopOnly = new List<ReconciliationEntry>();

        foreach (var provider in providerMessages)
        {
            if (eShopBySid.TryGetValue(provider.Sid, out var n))
                matched.Add(ToEntry(provider, n));
            else
                providerOnly.Add(ToEntry(provider, null));
        }

        foreach (var n in eShopNotifications)
        {
            if (n.ProviderMessageSid is null || !providerBySid.ContainsKey(n.ProviderMessageSid))
                eShopOnly.Add(ToEntry(null, n));
        }

        return new ReconciliationReport(from, to, _gateway.SendingNumber,
            providerMessages.Count, eShopNotifications.Count, matched, providerOnly, eShopOnly);
    }

    // ---------------- helpers ----------------

    private async Task NotifyAsync(Order order, NotificationKind kind, CancellationToken ct)
    {
        var contactNumbers = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(order.BuyerId), ct);
        if (contactNumbers.Count == 0)
        {
            _logger.LogInformation("Order {OrderId} {Kind}: no number on file, nothing sent.", order.Id, kind);
            return;
        }

        var body = NotificationMessages.For(kind, order.Id);
        foreach (var contactNumber in contactNumbers)
        {
            var notification = new OrderNotification(order.Id, order.BuyerId, contactNumber.CanonicalNumber, contactNumber.Id, kind, body);
            var result = await _gateway.SendAsync(contactNumber.CanonicalNumber, body, ct);
            if (result.Accepted && !string.IsNullOrEmpty(result.ProviderMessageSid))
                notification.MarkSentToProvider(result.ProviderMessageSid!, result.Status);
            else
                notification.MarkSendFailed(result.ErrorCode, result.ErrorMessage);

            await _notificationRepository.AddAsync(notification, ct);
            _logger.LogInformation("Order {OrderId} {Kind}: notification {NotificationId} (status {Status}).",
                order.Id, kind, notification.Id, notification.DeliveryStatus);
        }
    }

    private async Task ScheduleFollowUpAsync(Order order, CancellationToken ct)
    {
        var contactNumbers = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(order.BuyerId), ct);
        if (contactNumbers.Count == 0)
            return;

        var body = NotificationMessages.For(NotificationKind.DeliveryFollowUp, order.Id);
        var sendAt = DateTimeOffset.UtcNow.AddDays(_settings.FollowUpDelayDays);
        foreach (var contactNumber in contactNumbers)
        {
            var notification = new OrderNotification(order.Id, order.BuyerId, contactNumber.CanonicalNumber, contactNumber.Id, NotificationKind.DeliveryFollowUp, body);
            var result = await _gateway.ScheduleAsync(contactNumber.CanonicalNumber, body, sendAt, ct);
            if (result.Accepted && !string.IsNullOrEmpty(result.ProviderMessageSid))
                notification.MarkSentToProvider(result.ProviderMessageSid!, result.Status);
            else
                notification.MarkSendFailed(result.ErrorCode, result.ErrorMessage);

            await _notificationRepository.AddAsync(notification, ct);
            _logger.LogInformation("Order {OrderId} follow-up queued as notification {NotificationId} (status {Status}).",
                order.Id, notification.Id, notification.DeliveryStatus);
        }
    }

    private async Task RefreshDeliveryOutcomesAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken ct)
    {
        foreach (var notification in notifications)
        {
            // Only re-read the provider for messages it owns and whose outcome could still change.
            if (string.IsNullOrEmpty(notification.ProviderMessageSid) || SmsDeliveryStatus.IsTerminal(notification.DeliveryStatus))
                continue;

            try
            {
                var status = await _gateway.FetchStatusAsync(notification.ProviderMessageSid!, ct);
                if (status.Status != notification.DeliveryStatus || status.ErrorCode.HasValue)
                {
                    notification.UpdateDeliveryStatus(status.Status, status.ErrorCode, status.ErrorMessage);
                    await _notificationRepository.UpdateAsync(notification, ct);
                }
            }
            catch (SmsGatewayException ex)
            {
                // A best-effort refresh must never fail the read; keep the last known outcome.
                _logger.LogWarning("Could not refresh delivery outcome for notification {NotificationId}: {Reason}.",
                    notification.Id, ex.Message);
            }
        }
    }

    private static ReconciliationEntry ToEntry(ProviderMessageRecord? provider, OrderNotification? notification) =>
        new(
            Sid: provider?.Sid ?? notification!.ProviderMessageSid!,
            ProviderStatus: provider?.Status,
            ProviderDateSent: provider?.DateSent,
            NotificationId: notification?.Id,
            OrderId: notification?.OrderId,
            EShopStatus: notification?.DeliveryStatus,
            Kind: notification?.Kind);
}
