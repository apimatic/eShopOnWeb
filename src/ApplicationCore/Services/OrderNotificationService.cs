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

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    public static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private static readonly Address DefaultShipToAddress =
        new("123 Main St.", "Kent", "OH", "United States", "44240");

    private readonly IOrderService _orderService;
    private readonly IRepository<Order> _orderRepository;
    private readonly IReadRepository<Order> _orderReadRepository;
    private readonly IRepository<CatalogItem> _catalogItemRepository;
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IRepository<NotificationIdempotencyRecord> _idempotencyRepository;
    private readonly ITwilioGateway _twilioGateway;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IOrderService orderService,
        IRepository<Order> orderRepository,
        IReadRepository<Order> orderReadRepository,
        IRepository<CatalogItem> catalogItemRepository,
        IRepository<ContactNumber> contactNumberRepository,
        IRepository<OrderNotification> notificationRepository,
        IRepository<NotificationIdempotencyRecord> idempotencyRepository,
        ITwilioGateway twilioGateway,
        IAppLogger<OrderNotificationService> logger)
    {
        _orderService = orderService;
        _orderRepository = orderRepository;
        _orderReadRepository = orderReadRepository;
        _catalogItemRepository = catalogItemRepository;
        _contactNumberRepository = contactNumberRepository;
        _notificationRepository = notificationRepository;
        _idempotencyRepository = idempotencyRepository;
        _twilioGateway = twilioGateway;
        _logger = logger;
    }

    public async Task<ContactNumberResult> RegisterContactNumberAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw new InvalidContactNumberException();
        }

        var lookup = await _twilioGateway.LookupPhoneNumberAsync(phoneNumber.Trim(), cancellationToken);
        if (!lookup.Valid || string.IsNullOrEmpty(lookup.CanonicalPhoneNumber))
        {
            throw new InvalidContactNumberException();
        }

        var existing = await _contactNumberRepository.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndPhoneSpec(buyerId, lookup.CanonicalPhoneNumber), cancellationToken);
        if (existing != null)
        {
            throw new DuplicateException("This destination is already registered.");
        }

        var contact = new ContactNumber(buyerId, lookup.CanonicalPhoneNumber);
        await _contactNumberRepository.AddAsync(contact, cancellationToken);

        return ToContactNumberResult(contact);
    }

    public async Task<IReadOnlyList<ContactNumberResult>> ListContactNumbersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var contacts = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerIdSpec(buyerId), cancellationToken);
        return contacts.Select(ToContactNumberResult).ToList();
    }

    public async Task<bool> DeleteContactNumberAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        var contact = await _contactNumberRepository.GetByIdAsync(contactNumberId, cancellationToken);
        if (contact is null || contact.BuyerId != buyerId)
        {
            return false;
        }

        var scheduled = await _notificationRepository.ListAsync(
            new ScheduledNotificationsByContactNumberIdSpec(contact.Id), cancellationToken);
        foreach (var notification in scheduled)
        {
            await CancelProviderMessageAsync(notification, cancellationToken);
            await _notificationRepository.UpdateAsync(notification, cancellationToken);
        }

        await _contactNumberRepository.DeleteAsync(contact, cancellationToken);
        return true;
    }

    public async Task<ShopperOrderResult> PlaceOrderAsync(string buyerId, IReadOnlyList<CatalogOrderLine> lines, Address? shipTo, CancellationToken cancellationToken = default)
    {
        if (lines is null || lines.Count == 0)
        {
            throw new ArgumentException("An order must contain at least one item.");
        }

        if (lines.Any(l => l.Quantity < 1 || l.CatalogItemId < 1))
        {
            throw new ArgumentException("Each order line must have a catalog item id and a quantity of at least 1.");
        }

        var catalogItems = await _catalogItemRepository.ListAsync(
            new CatalogItemsSpecification(lines.Select(l => l.CatalogItemId).ToArray()), cancellationToken);
        if (catalogItems.Count != lines.Select(l => l.CatalogItemId).Distinct().Count())
        {
            throw new ArgumentException("One or more catalog items were not found.");
        }

        var order = await _orderService.CreateOrderFromCatalogItemsAsync(
            buyerId, shipTo ?? DefaultShipToAddress, lines);

        await NotifyAsync(order, OrderNotificationKind.OrderPlaced, NotificationMessageText.OrderPlaced(order.Id), sendAt: null, cancellationToken);

        return await BuildShopperOrderAsync(order, cancellationToken);
    }

    public async Task<ShopperOrderResult?> DispatchOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            return null;
        }

        order.MarkDispatched();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        await NotifyAsync(order, OrderNotificationKind.OrderDispatched, NotificationMessageText.OrderDispatched(order.Id), sendAt: null, cancellationToken);
        await NotifyAsync(
            order,
            OrderNotificationKind.DeliveryFollowUp,
            NotificationMessageText.DeliveryFollowUp(order.Id),
            DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay),
            cancellationToken);

        return await BuildShopperOrderAsync(order, cancellationToken);
    }

    public async Task<ShopperOrderResult?> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            return null;
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        var followUps = await _notificationRepository.ListAsync(new ScheduledFollowUpsByOrderIdSpec(order.Id), cancellationToken);
        foreach (var followUp in followUps)
        {
            await CancelProviderMessageAsync(followUp, cancellationToken);
            await _notificationRepository.UpdateAsync(followUp, cancellationToken);
        }

        await NotifyAsync(order, OrderNotificationKind.OrderCancelled, NotificationMessageText.OrderCancelled(order.Id), sendAt: null, cancellationToken);

        return await BuildShopperOrderAsync(order, cancellationToken);
    }

    public async Task<IReadOnlyList<ShopperOrderResult>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orderReadRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        if (orders.Count == 0)
        {
            return Array.Empty<ShopperOrderResult>();
        }

        var notifications = await _notificationRepository.ListAsync(
            new NotificationsByOrderIdsSpec(orders.Select(o => o.Id)), cancellationToken);
        await RefreshNotificationsAsync(notifications, cancellationToken);

        var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => g.ToList());
        return orders.Select(order => ToShopperOrder(order, byOrder.GetValueOrDefault(order.Id) ?? new List<OrderNotification>())).ToList();
    }

    public async Task<IReadOnlyList<NotificationResult>?> GetOrderNotificationsAsync(int orderId, string buyerId, bool isAdministrator, CancellationToken cancellationToken = default)
    {
        var order = await _orderReadRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        if (!isAdministrator && order.BuyerId != buyerId)
        {
            return null;
        }

        var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderIdSpec(orderId), cancellationToken);
        await RefreshNotificationsAsync(notifications, cancellationToken);
        return notifications.Select(ToNotificationResult).ToList();
    }

    public async Task<ResendNotificationResult?> ResendNotificationAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("An idempotency key is required.");
        }

        var source = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (source is null)
        {
            return null;
        }

        var existing = await _idempotencyRepository.FirstOrDefaultAsync(
            new NotificationIdempotencyByKeySpec(source.Id, idempotencyKey.Trim()), cancellationToken);
        if (existing != null)
        {
            return new ResendNotificationResult(existing.ResultNotificationId);
        }

        if (!source.DidNotReachShopper())
        {
            throw new InvalidOperationException("Only messages that did not reach the shopper can be re-sent.");
        }

        var destinationStillRegistered = await IsDestinationStillRegisteredAsync(source, cancellationToken);
        if (!destinationStillRegistered)
        {
            throw new InvalidOperationException("The original destination is no longer on file and cannot be messaged.");
        }

        var body = source.BodyForResend();
        var resend = new OrderNotification(
            source.OrderId,
            source.BuyerId,
            OrderNotificationKind.Resend,
            body,
            source.ContactNumberId,
            source.DestinationPhoneNumber,
            resendOfNotificationId: source.Id);

        await _notificationRepository.AddAsync(resend, cancellationToken);
        await DeliverAsync(resend, cancellationToken);
        await _notificationRepository.UpdateAsync(resend, cancellationToken);

        var record = new NotificationIdempotencyRecord(source.Id, idempotencyKey.Trim(), resend.Id);
        await _idempotencyRepository.AddAsync(record, cancellationToken);

        return new ResendNotificationResult(resend.Id);
    }

    public async Task<bool?> RedactNotificationContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            return null;
        }

        if (notification.HasProviderMessage)
        {
            try
            {
                var snapshot = await _twilioGateway.RedactMessageBodyAsync(notification.ProviderMessageSid!, cancellationToken);
                notification.RefreshFromProvider(snapshot.Status, snapshot.ErrorCode, snapshot.DateSent, snapshot.Body);
            }
        catch (TwilioGatewayException)
        {
            _logger.LogWarning("Failed to redact provider content for notification {NotificationId}: {ExceptionType}", notification.Id, nameof(TwilioGatewayException));
            throw;
        }
        }

        notification.MarkContentRedacted();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
        return true;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new ArgumentException("The 'to' timestamp must be on or after 'from'.");
        }

        var fromNumber = _twilioGateway.ConfiguredFromNumber;
        var providerMessages = await _twilioGateway.ListMessagesFromNumberAsync(fromNumber, from, to, cancellationToken);
        var applicationNotifications = await _notificationRepository.ListAsync(new NotificationsCreatedBetweenSpec(from, to), cancellationToken);
        await RefreshNotificationsAsync(applicationNotifications, cancellationToken);

        var items = new List<ReconciliationItem>();
        var matchedSids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var provider in providerMessages)
        {
            var local = applicationNotifications.FirstOrDefault(n =>
                !string.IsNullOrEmpty(n.ProviderMessageSid) &&
                string.Equals(n.ProviderMessageSid, provider.Sid, StringComparison.Ordinal));

            if (local != null)
            {
                matchedSids.Add(provider.Sid);
                items.Add(new ReconciliationItem(
                    provider.Sid,
                    local.Id,
                    "matched",
                    provider.Status,
                    local.ProviderStatus,
                    provider.DateSent,
                    local.CreatedAt));
            }
            else
            {
                items.Add(new ReconciliationItem(
                    provider.Sid,
                    null,
                    "provider_only",
                    provider.Status,
                    null,
                    provider.DateSent,
                    null));
            }
        }

        foreach (var local in applicationNotifications)
        {
            if (string.IsNullOrEmpty(local.ProviderMessageSid) || !matchedSids.Contains(local.ProviderMessageSid))
            {
                if (!string.IsNullOrEmpty(local.ProviderMessageSid) &&
                    providerMessages.Any(p => p.Sid == local.ProviderMessageSid))
                {
                    continue;
                }

                items.Add(new ReconciliationItem(
                    local.ProviderMessageSid,
                    local.Id,
                    string.IsNullOrEmpty(local.ProviderMessageSid) ? "application_only" : "application_only",
                    null,
                    local.ProviderStatus,
                    local.ProviderDateSent,
                    local.CreatedAt));
            }
        }

        return new ReconciliationReport(from, to, fromNumber, items);
    }

    private async Task NotifyAsync(
        Order order,
        OrderNotificationKind kind,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        var destination = await GetActiveDestinationAsync(order.BuyerId, cancellationToken);
        var notification = new OrderNotification(
            order.Id,
            order.BuyerId,
            kind,
            body,
            destination?.Id,
            destination?.PhoneNumber,
            sendAt);

        await _notificationRepository.AddAsync(notification, cancellationToken);

        if (destination is null)
        {
            _logger.LogInformation("Skipping SMS for order {OrderId} notification {NotificationId}; no destination on file.", order.Id, notification.Id);
            return;
        }

        await DeliverAsync(notification, cancellationToken);
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
    }

    private async Task DeliverAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(notification.DestinationPhoneNumber))
        {
            return;
        }

        try
        {
            var snapshot = await _twilioGateway.SendMessageAsync(
                notification.DestinationPhoneNumber,
                notification.Body ?? string.Empty,
                notification.ScheduledSendAt,
                cancellationToken);
            notification.ApplyProviderResult(snapshot.Sid, snapshot.Status, snapshot.ErrorCode, snapshot.DateSent);
            _logger.LogInformation(
                "Recorded provider message {MessageSid} for notification {NotificationId} on order {OrderId} with status {Status}.",
                snapshot.Sid,
                notification.Id,
                notification.OrderId,
                snapshot.Status);
        }
        catch (Exception ex)
        {
            notification.MarkSendFailed();
            _logger.LogWarning(
                "Failed to send notification {NotificationId} for order {OrderId}: {ExceptionType}",
                notification.Id,
                notification.OrderId,
                ex.GetType().Name);
        }
    }

    private async Task CancelProviderMessageAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        if (!notification.HasProviderMessage)
        {
            return;
        }

        try
        {
            var snapshot = await _twilioGateway.CancelScheduledMessageAsync(notification.ProviderMessageSid!, cancellationToken);
            notification.ApplyProviderResult(snapshot.Sid, snapshot.Status, snapshot.ErrorCode, snapshot.DateSent);
            _logger.LogInformation(
                "Cancelled provider message {MessageSid} for notification {NotificationId} on order {OrderId}.",
                snapshot.Sid,
                notification.Id,
                notification.OrderId);
        }
        catch (TwilioGatewayException ex)
        {
            _logger.LogWarning(
                "Provider rejected cancel for notification {NotificationId} on order {OrderId}: HTTP {StatusCode} provider {ProviderCode}. Fetching current status.",
                notification.Id,
                notification.OrderId,
                ex.HttpStatus,
                ex.ProviderErrorCode.HasValue ? ex.ProviderErrorCode.Value : 0);
            await TryRefreshOneAsync(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Failed to cancel provider message for notification {NotificationId} on order {OrderId}: {ExceptionType}",
                notification.Id,
                notification.OrderId,
                ex.GetType().Name);
        }
    }

    private async Task RefreshNotificationsAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications.Where(n => n.HasProviderMessage))
        {
            await TryRefreshOneAsync(notification, cancellationToken);
            await _notificationRepository.UpdateAsync(notification, cancellationToken);
        }
    }

    private async Task TryRefreshOneAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        if (!notification.HasProviderMessage)
        {
            return;
        }

        try
        {
            var snapshot = await _twilioGateway.FetchMessageAsync(notification.ProviderMessageSid!, cancellationToken);
            notification.RefreshFromProvider(snapshot.Status, snapshot.ErrorCode, snapshot.DateSent, snapshot.Body);
            if (notification.ContentRedacted)
            {
                notification.MarkContentRedacted();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Failed to refresh provider status for notification {NotificationId}: {ExceptionType}",
                notification.Id,
                ex.GetType().Name);
        }
    }

    private async Task<ContactNumber?> GetActiveDestinationAsync(string buyerId, CancellationToken cancellationToken)
    {
        var contacts = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerIdSpec(buyerId), cancellationToken);
        return contacts.FirstOrDefault();
    }

    private async Task<bool> IsDestinationStillRegisteredAsync(OrderNotification source, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(source.DestinationPhoneNumber))
        {
            return false;
        }

        var match = await _contactNumberRepository.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndPhoneSpec(source.BuyerId, source.DestinationPhoneNumber), cancellationToken);
        return match != null;
    }

    private async Task<ShopperOrderResult> BuildShopperOrderAsync(Order order, CancellationToken cancellationToken)
    {
        var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderIdSpec(order.Id), cancellationToken);
        await RefreshNotificationsAsync(notifications, cancellationToken);
        return ToShopperOrder(order, notifications);
    }

    private static ShopperOrderResult ToShopperOrder(Order order, IReadOnlyList<OrderNotification> notifications)
    {
        var items = order.OrderItems.Select(i =>
            new OrderLineResult(i.ItemOrdered.CatalogItemId, i.ItemOrdered.ProductName, i.UnitPrice, i.Units)).ToList();
        return new ShopperOrderResult(
            order.Id,
            order.Status.ToString(),
            order.OrderDate,
            order.Total(),
            items,
            notifications.Select(ToNotificationResult).ToList());
    }

    private static NotificationResult ToNotificationResult(OrderNotification notification)
    {
        return new NotificationResult(
            notification.Id,
            notification.OrderId,
            notification.Kind.ToString(),
            notification.ContentRedacted ? null : notification.Body,
            notification.ContentRedacted,
            notification.ProviderMessageSid,
            notification.ProviderStatus,
            notification.ProviderErrorCode,
            notification.ScheduledSendAt,
            notification.CreatedAt,
            notification.ProviderDateSent);
    }

    private static ContactNumberResult ToContactNumberResult(ContactNumber contact)
    {
        return new ContactNumberResult(contact.Id, contact.PhoneNumber);
    }
}
