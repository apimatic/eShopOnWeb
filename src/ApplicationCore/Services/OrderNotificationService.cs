using System;
using System.Collections.Generic;
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

public class OrderNotificationService : IOrderNotificationService
{
    // The API collects only catalog items + quantities, not an address; the Order aggregate requires
    // a ship-to address, so a clearly-labelled placeholder is supplied.
    private static readonly Address PlaceholderAddress = new("Not provided", "Not provided", "Not provided", "Not provided", "00000");

    // How far after dispatch the "how did delivery go?" survey is queued (within the provider's scheduling window).
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly ISmsGateway _gateway;
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        ISmsGateway gateway,
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<ContactNumber> contactNumberRepository,
        IRepository<OrderNotification> notificationRepository,
        IUriComposer uriComposer,
        IAppLogger<OrderNotificationService> logger)
    {
        _gateway = gateway;
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _contactNumberRepository = contactNumberRepository;
        _notificationRepository = notificationRepository;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    // ----------------------------------------------------------------- Contact numbers

    public async Task<ContactNumber> RegisterNumberAsync(string ownerId, string rawNumber, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(rawNumber, nameof(rawNumber));

        var validation = await _gateway.ValidateAsync(rawNumber, ct);
        if (!validation.IsUsable || string.IsNullOrEmpty(validation.CanonicalNumber))
        {
            throw new ContactNumberRejectedException("The number is not a usable SMS destination and was not registered.");
        }

        var contactNumber = new ContactNumber(ownerId, validation.CanonicalNumber);
        await _contactNumberRepository.AddAsync(contactNumber, ct);
        _logger.LogInformation("Registered contact number {Id} for a shopper.", contactNumber.Id);
        return contactNumber;
    }

    public async Task<IReadOnlyList<ContactNumber>> GetNumbersAsync(string ownerId, CancellationToken ct)
        => await _contactNumberRepository.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), ct);

    public async Task<bool> DeleteNumberAsync(string ownerId, int contactNumberId, CancellationToken ct)
    {
        var existing = await _contactNumberRepository.FirstOrDefaultAsync(
            new ContactNumberByIdForOwnerSpecification(contactNumberId, ownerId), ct);
        if (existing is null)
        {
            return false;
        }

        await _contactNumberRepository.DeleteAsync(existing, ct);
        _logger.LogInformation("Removed contact number {Id}.", contactNumberId);
        return true;
    }

    // ----------------------------------------------------------------- Orders

    public async Task<Order> PlaceOrderAsync(string ownerId, IReadOnlyList<OrderLineRequest> lines, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.Null(lines, nameof(lines));
        if (lines.Count == 0)
        {
            throw new ArgumentException("An order must contain at least one line.", nameof(lines));
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), ct);

        var items = new List<OrderItem>();
        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new ArgumentException($"Quantity for catalog item {line.CatalogItemId} must be greater than zero.", nameof(lines));
            }

            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw new ArgumentException($"Catalog item {line.CatalogItemId} does not exist.", nameof(lines));

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            items.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(ownerId, PlaceholderAddress, items);
        await _orderRepository.AddAsync(order, ct);
        _logger.LogInformation("Placed order {OrderId} for a shopper.", order.Id);

        await NotifyAsync(order.Id, ownerId, NotificationKind.OrderPlaced, MessageBody(NotificationKind.OrderPlaced, order.Id), schedule: false, ct);
        return order;
    }

    public async Task<Order?> DispatchOrderAsync(int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct);
        if (order is null)
        {
            return null;
        }

        _logger.LogInformation("Order {OrderId} dispatched.", orderId);
        await NotifyAsync(orderId, order.BuyerId, NotificationKind.OrderDispatched, MessageBody(NotificationKind.OrderDispatched, orderId), schedule: false, ct);
        // Queue the "how did delivery go?" survey with the provider for a few days later.
        await NotifyAsync(orderId, order.BuyerId, NotificationKind.DeliveryFollowUp, MessageBody(NotificationKind.DeliveryFollowUp, orderId), schedule: true, ct);
        return order;
    }

    public async Task<Order?> CancelOrderAsync(int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct);
        if (order is null)
        {
            return null;
        }

        _logger.LogInformation("Order {OrderId} cancelled.", orderId);
        await NotifyAsync(orderId, order.BuyerId, NotificationKind.OrderCancelled, MessageBody(NotificationKind.OrderCancelled, orderId), schedule: false, ct);

        // Call off any follow-up survey that has not yet gone out.
        var scheduled = await _notificationRepository.ListAsync(new ScheduledFollowUpForOrderSpecification(orderId), ct);
        foreach (var followUp in scheduled)
        {
            try
            {
                await _gateway.CancelScheduledAsync(followUp.ProviderSid!, ct);
                followUp.MarkCancelled();
                await _notificationRepository.UpdateAsync(followUp, ct);
                _logger.LogInformation("Called off scheduled follow-up notification {Id} for order {OrderId}.", followUp.Id, orderId);
            }
            catch (SmsGatewayException ex)
            {
                // Surface loudly, but never fail the cancel operation itself.
                _logger.LogWarning("Could not call off scheduled follow-up notification {Id} for order {OrderId}: {Error}", followUp.Id, orderId, ex.Message);
            }
        }

        return order;
    }

    // ----------------------------------------------------------------- Reads

    public async Task<Order?> GetOrderForOwnerAsync(int orderId, string ownerId, CancellationToken ct)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct);
        return order is not null && order.BuyerId == ownerId ? order : null;
    }

    public async Task<IReadOnlyList<Order>> GetOrdersForOwnerAsync(string ownerId, CancellationToken ct)
        => await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(ownerId), ct);

    public async Task<IReadOnlyList<OrderNotification>> GetNotificationsForOrderAsync(int orderId, bool refreshFromProvider, CancellationToken ct)
    {
        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByOrderSpecification(orderId), ct);
        if (refreshFromProvider)
        {
            await RefreshAsync(notifications, ct);
        }
        return notifications;
    }

    public async Task<IReadOnlyList<OrderNotification>> GetNotificationsForOwnerAsync(string ownerId, bool refreshFromProvider, CancellationToken ct)
    {
        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByOwnerSpecification(ownerId), ct);
        if (refreshFromProvider)
        {
            await RefreshAsync(notifications, ct);
        }
        return notifications;
    }

    // ----------------------------------------------------------------- Operator actions

    public async Task<ResendOutcome?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        // Repeating a request under the same key must not send a second message.
        var replay = await _notificationRepository.FirstOrDefaultAsync(new OrderNotificationByResendKeySpecification(idempotencyKey), ct);
        if (replay is not null)
        {
            _logger.LogInformation("Resend replay for idempotency key; returning existing notification {Id}.", replay.Id);
            return new ResendOutcome(replay, WasReplay: true);
        }

        var original = await _notificationRepository.GetByIdAsync(notificationId, ct);
        if (original is null)
        {
            return null;
        }

        if (original.ContentDisposed || string.IsNullOrEmpty(original.Body))
        {
            throw new InvalidOperationException("The message content has been disposed of and can no longer be re-sent.");
        }

        var resend = OrderNotification.ForImmediate(original.OrderId, original.OwnerId, original.Kind, original.To, original.Body!);
        resend.MarkAsResendOf(original.Id, idempotencyKey);
        try
        {
            var result = await _gateway.SendAsync(original.To, original.Body!, ct);
            resend.RecordAccepted(result.ProviderSid, result.From, result.Status, result.DateSent);
            _logger.LogInformation("Re-sent notification {OriginalId} as {NewId}.", original.Id, resend.Id);
        }
        catch (SmsGatewayException ex)
        {
            resend.RecordSendFailed(ex.Message);
            _logger.LogWarning("Resend of notification {OriginalId} failed to send: {Error}", original.Id, ex.Message);
        }

        await _notificationRepository.AddAsync(resend, ct);
        return new ResendOutcome(resend, WasReplay: false);
    }

    public async Task<bool> DisposeContentAsync(int notificationId, CancellationToken ct)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, ct);
        if (notification is null)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(notification.ProviderSid))
        {
            // Redact at the provider so the text can no longer be retrieved there either.
            await _gateway.RedactContentAsync(notification.ProviderSid, ct);
        }

        notification.DisposeContent();
        await _notificationRepository.UpdateAsync(notification, ct);
        _logger.LogInformation("Disposed of the content of notification {Id}.", notificationId);
        return true;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var sendingNumber = _gateway.SendingNumber;
        var providerMessages = await _gateway.ListSentFromConfiguredNumberAsync(from, to, ct);

        // eShop's side of the ledger: notifications it believes the provider accepted from the sending
        // number, whose recorded send time falls in the range.
        var allNotifications = await _notificationRepository.ListAsync(ct);
        var eshopBelieved = allNotifications
            .Where(n => !string.IsNullOrEmpty(n.ProviderSid)
                        && string.Equals(n.ProviderFrom, sendingNumber, StringComparison.Ordinal)
                        && InRange(n.ProviderDateSent ?? n.CreatedAt, from, to))
            .ToList();

        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.ProviderSid))
            .GroupBy(m => m.ProviderSid!)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var eshopBySid = eshopBelieved.ToDictionary(n => n.ProviderSid!, n => n, StringComparer.Ordinal);

        var matched = new List<ReconciliationMatch>();
        foreach (var n in eshopBelieved)
        {
            if (providerBySid.TryGetValue(n.ProviderSid!, out var provider))
            {
                matched.Add(new ReconciliationMatch(n.ProviderSid!, n.Id, n.OrderId, provider.Status, n.DeliveryStatus));
            }
        }

        var eshopOnly = eshopBelieved
            .Where(n => !providerBySid.ContainsKey(n.ProviderSid!))
            .Select(n => new ReconciliationEshopOnly(n.Id, n.OrderId, n.ProviderSid!, n.DeliveryStatus))
            .ToList();

        var providerOnly = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.ProviderSid) && !eshopBySid.ContainsKey(m.ProviderSid!))
            .ToList();

        _logger.LogInformation(
            "Reconciliation {From:o}..{To:o}: {Matched} matched, {ProviderOnly} provider-only, {EshopOnly} eShop-only.",
            from, to, matched.Count, providerOnly.Count, eshopOnly.Count);

        return new ReconciliationReport(from, to, sendingNumber, matched, providerOnly, eshopOnly);
    }

    // ----------------------------------------------------------------- Helpers

    /// <summary>
    /// Sends (or schedules) one message per registered number of the shopper, recording each attempt.
    /// A send failure is recorded and swallowed so it never fails the underlying order operation. A
    /// shopper with no number on file is simply not messaged.
    /// </summary>
    private async Task NotifyAsync(int orderId, string ownerId, NotificationKind kind, string body, bool schedule, CancellationToken ct)
    {
        var numbers = await _contactNumberRepository.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), ct);
        if (numbers.Count == 0)
        {
            _logger.LogInformation("No contact number on file for order {OrderId}; {Kind} message not sent.", orderId, kind);
            return;
        }

        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        foreach (var number in numbers)
        {
            var notification = schedule
                ? OrderNotification.ForScheduled(orderId, ownerId, kind, number.PhoneNumber, body, sendAt)
                : OrderNotification.ForImmediate(orderId, ownerId, kind, number.PhoneNumber, body);
            try
            {
                var result = schedule
                    ? await _gateway.ScheduleAsync(number.PhoneNumber, body, sendAt, ct)
                    : await _gateway.SendAsync(number.PhoneNumber, body, ct);
                notification.RecordAccepted(result.ProviderSid, result.From, result.Status, result.DateSent);
                _logger.LogInformation("{Kind} notification {Id} accepted for order {OrderId} (provider {Sid}, status {Status}).",
                    kind, notification.Id, orderId, result.ProviderSid, result.Status);
            }
            catch (SmsGatewayException ex)
            {
                notification.RecordSendFailed(ex.Message);
                _logger.LogWarning("{Kind} notification for order {OrderId} could not be sent: {Error}", kind, orderId, ex.Message);
            }

            await _notificationRepository.AddAsync(notification, ct);
        }
    }

    private async Task RefreshAsync(IEnumerable<OrderNotification> notifications, CancellationToken ct)
    {
        foreach (var n in notifications)
        {
            if (string.IsNullOrEmpty(n.ProviderSid) || IsTerminal(n.DeliveryStatus))
            {
                continue;
            }

            try
            {
                var state = await _gateway.GetDeliveryStateAsync(n.ProviderSid, ct);
                n.UpdateDeliveryState(state.Status, state.ErrorCode, state.ErrorMessage, state.DateSent);
                await _notificationRepository.UpdateAsync(n, ct);
            }
            catch (SmsGatewayException ex)
            {
                // Best-effort refresh: keep the last-known outcome if the provider read fails.
                _logger.LogWarning("Could not refresh delivery state for notification {Id}: {Error}", n.Id, ex.Message);
            }
        }
    }

    private static bool InRange(DateTimeOffset value, DateTimeOffset from, DateTimeOffset to)
        => value >= from && value <= to;

    private static bool IsTerminal(string? status) => status is "delivered" or "undelivered" or "failed" or "canceled";

    private static string MessageBody(NotificationKind kind, int orderId) => kind switch
    {
        NotificationKind.OrderPlaced => $"eShop: your order #{orderId} has been placed. Thank you!",
        NotificationKind.OrderDispatched => $"eShop: your order #{orderId} is on its way!",
        NotificationKind.OrderCancelled => $"eShop: your order #{orderId} has been cancelled.",
        NotificationKind.DeliveryFollowUp => $"eShop: how did the delivery of your order #{orderId} go? We'd love your feedback.",
        _ => $"eShop: an update about your order #{orderId}."
    };
}
