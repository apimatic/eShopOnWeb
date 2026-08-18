using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
    /// <summary>How far ahead the delivery follow-up is queued. Comfortably inside the provider's
    /// 15-minute-to-35-day scheduling window.</summary>
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    /// <summary>Provider statuses that are settled — no point re-fetching them.</summary>
    private static readonly HashSet<string> TerminalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "delivered", "undelivered", "failed", "canceled", "read", OrderNotification.NotSentStatus
    };

    private readonly IRepository<Order> _orderRepository;
    private readonly IReadRepository<CatalogItem> _catalogRepository;
    private readonly IRepository<ContactNumber> _contactRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly ISmsProvider _smsProvider;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orderRepository,
        IReadRepository<CatalogItem> catalogRepository,
        IRepository<ContactNumber> contactRepository,
        IRepository<OrderNotification> notificationRepository,
        ISmsProvider smsProvider,
        IUriComposer uriComposer,
        IAppLogger<OrderNotificationService> logger)
    {
        _orderRepository = orderRepository;
        _catalogRepository = catalogRepository;
        _contactRepository = contactRepository;
        _notificationRepository = notificationRepository;
        _smsProvider = smsProvider;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    // ---- Flow 1 -----------------------------------------------------------------------------

    public async Task<ContactNumber> RegisterContactNumberAsync(string ownerId, string rawNumber, string? countryCode, CancellationToken cancellationToken = default)
    {
        var validation = await _smsProvider.ValidateNumberAsync(rawNumber, countryCode, cancellationToken);
        if (!validation.IsValid || string.IsNullOrEmpty(validation.E164))
        {
            throw new InvalidContactNumberException(validation.ValidationErrors);
        }

        var contactNumber = new ContactNumber(ownerId, validation.E164, validation.NationalFormat, validation.CountryCode);
        await _contactRepository.AddAsync(contactNumber, cancellationToken);
        _logger.LogInformation("Registered contact number {ContactNumberId} for a shopper.", contactNumber.Id);
        return contactNumber;
    }

    public async Task<IReadOnlyList<ContactNumber>> GetContactNumbersAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        var numbers = await _contactRepository.ListAsync(new ContactNumbersByOwnerSpec(ownerId), cancellationToken);
        return numbers;
    }

    public async Task<bool> DeleteContactNumberAsync(string ownerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        var contactNumber = await _contactRepository.FirstOrDefaultAsync(new ContactNumberByIdForOwnerSpec(contactNumberId, ownerId), cancellationToken);
        if (contactNumber is null) return false;

        await _contactRepository.DeleteAsync(contactNumber, cancellationToken);
        _logger.LogInformation("Deleted contact number {ContactNumberId}.", contactNumberId);
        return true;
    }

    // ---- Flow 2 -----------------------------------------------------------------------------

    public async Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> lines, ShippingAddressRequest? shippingAddress, CancellationToken cancellationToken = default)
    {
        if (lines is null || lines.Count == 0)
        {
            throw new OrderPlacementException("An order must contain at least one item.");
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);

        var items = new List<OrderItem>();
        foreach (var line in lines)
        {
            if (line.Quantity < 1)
            {
                throw new OrderPlacementException($"Quantity for catalog item {line.CatalogItemId} must be at least 1.");
            }

            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId);
            if (catalogItem is null)
            {
                throw new OrderPlacementException($"Catalog item {line.CatalogItemId} does not exist.");
            }

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            items.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var address = shippingAddress is null
            ? new Address("N/A", "N/A", "N/A", "N/A", "00000")
            : new Address(shippingAddress.Street, shippingAddress.City, shippingAddress.State, shippingAddress.Country, shippingAddress.ZipCode);

        var order = new Order(buyerId, address, items);
        await _orderRepository.AddAsync(order, cancellationToken);
        _logger.LogInformation("Placed order {OrderId} for a shopper.", order.Id);

        await NotifyOwnerAsync(order.Id, buyerId, OrderNotificationType.OrderPlaced, BuildBody(OrderNotificationType.OrderPlaced, order.Id), cancellationToken);

        return order.Id;
    }

    public async Task<bool> DispatchOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null) return false;

        // Tell the shopper it's on its way.
        await NotifyOwnerAsync(orderId, order.BuyerId, OrderNotificationType.OrderDispatched, BuildBody(OrderNotificationType.OrderDispatched, orderId), cancellationToken);

        // Queue the delivery follow-up with the provider for a few days later.
        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        var numbers = await _contactRepository.ListAsync(new ContactNumbersByOwnerSpec(order.BuyerId), cancellationToken);
        foreach (var number in numbers)
        {
            var body = BuildBody(OrderNotificationType.DeliveryFollowUp, orderId);
            var notification = new OrderNotification(orderId, order.BuyerId, number.PhoneNumber, OrderNotificationType.DeliveryFollowUp, body);
            await _notificationRepository.AddAsync(notification, cancellationToken);
            try
            {
                var result = await _smsProvider.ScheduleAsync(number.PhoneNumber, body, sendAt, cancellationToken);
                notification.RecordScheduled(result.Sid, result.Status, sendAt);
                _logger.LogInformation("Scheduled follow-up {NotificationId} ({Sid}) for order {OrderId}.", notification.Id, result.Sid, orderId);
            }
            catch (Exception ex)
            {
                notification.RecordNotSent();
                _logger.LogWarning("Could not schedule follow-up {NotificationId} for order {OrderId}: {Error}", notification.Id, orderId, ex.Message);
            }
            await _notificationRepository.UpdateAsync(notification, cancellationToken);
        }

        return true;
    }

    public async Task<bool> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null) return false;

        // Call off any not-yet-sent follow-up first, so a cancelled order can never trigger a
        // "how did delivery go?" message.
        var existing = await _notificationRepository.ListAsync(new OrderNotificationsByOrderSpec(orderId), cancellationToken);
        foreach (var notification in existing.Where(n => n.IsScheduled && n.ProviderMessageSid is not null && !IsTerminal(n.ProviderStatus)))
        {
            try
            {
                await _smsProvider.CancelScheduledAsync(notification.ProviderMessageSid!, cancellationToken);
                notification.UpdateProviderStatus("canceled", null);
                _logger.LogInformation("Cancelled scheduled follow-up {NotificationId} ({Sid}) for order {OrderId}.", notification.Id, notification.ProviderMessageSid, orderId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not cancel scheduled follow-up {NotificationId} for order {OrderId}: {Error}", notification.Id, orderId, ex.Message);
            }
            await _notificationRepository.UpdateAsync(notification, cancellationToken);
        }

        // Then tell the shopper the order was cancelled.
        await NotifyOwnerAsync(orderId, order.BuyerId, OrderNotificationType.OrderCancelled, BuildBody(OrderNotificationType.OrderCancelled, orderId), cancellationToken);

        return true;
    }

    public async Task<IReadOnlyList<Order>> GetOrdersForOwnerAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        return await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(ownerId), cancellationToken);
    }

    public async Task<Order?> GetOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        return await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>> GetNotificationsForOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByOrderSpec(orderId), cancellationToken);
        await RefreshStatusesAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<IReadOnlyList<OrderNotification>> GetNotificationsForOrdersAsync(int[] orderIds, CancellationToken cancellationToken = default)
    {
        if (orderIds.Length == 0) return Array.Empty<OrderNotification>();
        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByOrderIdsSpec(orderIds), cancellationToken);
        await RefreshStatusesAsync(notifications, cancellationToken);
        return notifications;
    }

    // ---- Flow 3 -----------------------------------------------------------------------------

    public async Task<ResendOutcome?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        // A repeat under the same key must not send a second message.
        var alreadyDone = await _notificationRepository.FirstOrDefaultAsync(new OrderNotificationByIdempotencyKeySpec(idempotencyKey), cancellationToken);
        if (alreadyDone is not null)
        {
            _logger.LogInformation("Resend under idempotency key already satisfied by notification {NotificationId}.", alreadyDone.Id);
            return new ResendOutcome(alreadyDone.Id, false);
        }

        var original = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (original is null) return null;

        var body = original.Body ?? BuildBody(original.Type, original.OrderId);
        var resend = new OrderNotification(original.OrderId, original.OwnerId, original.ToPhoneNumber, original.Type, body);
        resend.SetIdempotencyKey(idempotencyKey);
        resend.SetResendOf(original.Id);
        await _notificationRepository.AddAsync(resend, cancellationToken);

        try
        {
            var result = await _smsProvider.SendAsync(original.ToPhoneNumber, body, cancellationToken);
            resend.RecordSent(result.Sid, result.Status, result.ErrorCode);
            _logger.LogInformation("Resent notification {ResendId} ({Sid}) as a re-send of {OriginalId}.", resend.Id, result.Sid, original.Id);
        }
        catch (Exception ex)
        {
            resend.RecordNotSent();
            _logger.LogWarning("Could not resend notification {OriginalId} (new {ResendId}): {Error}", original.Id, resend.Id, ex.Message);
        }
        await _notificationRepository.UpdateAsync(resend, cancellationToken);

        return new ResendOutcome(resend.Id, true);
    }

    public async Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null) return false;

        if (notification.ProviderMessageSid is not null)
        {
            // Redact at the provider so the text can no longer be retrieved there either.
            await _smsProvider.RedactContentAsync(notification.ProviderMessageSid, cancellationToken);
        }

        notification.Redact();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
        _logger.LogInformation("Disposed of the content of notification {NotificationId}.", notificationId);
        return true;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var fromNumber = _smsProvider.FromNumber;
        var providerMessages = await _smsProvider.ListSentMessagesAsync(from, to, cancellationToken);

        // Narrow to the exact window (the provider list filter is day-granular).
        var provider = providerMessages
            .Where(m => !m.DateSent.HasValue || (m.DateSent.Value >= from && m.DateSent.Value <= to))
            .ToList();

        var providerSids = new HashSet<string>(provider.Select(m => m.Sid), StringComparer.OrdinalIgnoreCase);

        // What eShop believes it sent in this window: notifications with a provider SID whose send
        // time falls in the range.
        var localSent = (await _notificationRepository.ListAsync(cancellationToken))
            .Where(n => n.ProviderMessageSid is not null && n.SentAt.HasValue && n.SentAt.Value >= from && n.SentAt.Value <= to)
            .ToList();
        var localSids = new HashSet<string>(localSent.Select(n => n.ProviderMessageSid!), StringComparer.OrdinalIgnoreCase);

        var providerEntries = provider
            .Select(m => new ReconciliationProviderEntry(
                m.Sid, m.Status, m.From, MaskNumber(m.To), m.DateSent, m.ErrorCode,
                KnownToEShop: localSids.Contains(m.Sid)))
            .ToList();

        var inProviderNotInEShop = provider
            .Where(m => !localSids.Contains(m.Sid))
            .Select(m => m.Sid)
            .ToList();

        var inEShopNotInProvider = localSent
            .Where(n => !providerSids.Contains(n.ProviderMessageSid!))
            .Select(n => new ReconciliationEShopEntry(
                n.Id, n.OrderId, n.ProviderMessageSid, n.ProviderStatus, n.SentAt,
                KnownToProvider: false))
            .ToList();

        return new ReconciliationReport(
            from, to, fromNumber,
            provider.Count, localSent.Count,
            providerEntries, inProviderNotInEShop, inEShopNotInProvider);
    }

    // ---- helpers ----------------------------------------------------------------------------

    private async Task NotifyOwnerAsync(int orderId, string ownerId, OrderNotificationType type, string body, CancellationToken cancellationToken)
    {
        var numbers = await _contactRepository.ListAsync(new ContactNumbersByOwnerSpec(ownerId), cancellationToken);
        if (numbers.Count == 0)
        {
            // A shopper with no number on file is simply not messaged.
            _logger.LogInformation("No contact number on file for order {OrderId}; skipping {Type} message.", orderId, type);
            return;
        }

        foreach (var number in numbers)
        {
            var notification = new OrderNotification(orderId, ownerId, number.PhoneNumber, type, body);
            await _notificationRepository.AddAsync(notification, cancellationToken);
            try
            {
                var result = await _smsProvider.SendAsync(number.PhoneNumber, body, cancellationToken);
                notification.RecordSent(result.Sid, result.Status, result.ErrorCode);
                _logger.LogInformation("Sent {Type} notification {NotificationId} ({Sid}) for order {OrderId}.", type, notification.Id, result.Sid, orderId);
            }
            catch (Exception ex)
            {
                // A message that cannot be sent must never fail the underlying operation.
                notification.RecordNotSent();
                _logger.LogWarning("Could not send {Type} notification {NotificationId} for order {OrderId}: {Error}", type, notification.Id, orderId, ex.Message);
            }
            await _notificationRepository.UpdateAsync(notification, cancellationToken);
        }
    }

    private async Task RefreshStatusesAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (notification.ProviderMessageSid is null || IsTerminal(notification.ProviderStatus))
            {
                continue;
            }

            try
            {
                var latest = await _smsProvider.FetchAsync(notification.ProviderMessageSid, cancellationToken);
                notification.UpdateProviderStatus(latest.Status, latest.ErrorCode);
                await _notificationRepository.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not refresh status of notification {NotificationId}: {Error}", notification.Id, ex.Message);
            }
        }
    }

    private static bool IsTerminal(string? status) => status is not null && TerminalStatuses.Contains(status);

    private static string BuildBody(OrderNotificationType type, int orderId) => type switch
    {
        OrderNotificationType.OrderPlaced => $"eShop: your order #{orderId} has been placed. Thank you for shopping with us!",
        OrderNotificationType.OrderDispatched => $"eShop: good news - your order #{orderId} is on its way!",
        OrderNotificationType.DeliveryFollowUp => $"eShop: how did the delivery of your order #{orderId} go? We'd love your feedback.",
        OrderNotificationType.OrderCancelled => $"eShop: your order #{orderId} has been cancelled. If this wasn't expected, please contact support.",
        _ => $"eShop: an update about your order #{orderId}."
    };

    private static string? MaskNumber(string? number)
    {
        if (string.IsNullOrEmpty(number)) return number;
        if (number.Length <= 4) return new string('*', number.Length);
        return string.Concat(new string('*', number.Length - 4), number.AsSpan(number.Length - 4));
    }
}
