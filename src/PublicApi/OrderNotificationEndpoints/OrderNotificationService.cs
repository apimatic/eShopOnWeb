using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

public sealed class OrderNotificationService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ResendLocks = new(StringComparer.Ordinal);
    private static readonly HashSet<string> ResendableStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "failed",
        "undelivered",
        "provider-error"
    };

    private readonly CatalogContext _db;
    private readonly ITextMessageProvider _provider;
    private readonly TimeProvider _timeProvider;

    public OrderNotificationService(CatalogContext db, ITextMessageProvider provider, TimeProvider timeProvider)
    {
        _db = db;
        _provider = provider;
        _timeProvider = timeProvider;
    }

    public async Task<RegisterContactNumberResponse> RegisterContactNumberAsync(
        string buyerId,
        RegisterContactNumberRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            throw BadRequest("A phoneNumber is required.");
        }

        ValidatedDestination validation;
        try
        {
            validation = await _provider.ValidateDestinationAsync(request.PhoneNumber, cancellationToken);
        }
        catch (TextMessageProviderException)
        {
            throw new OrderNotificationApiException(503, "The phone number could not be validated at this time.");
        }

        if (!validation.IsValid || string.IsNullOrWhiteSpace(validation.CanonicalNumber))
        {
            throw BadRequest("The messaging provider does not consider this a valid destination.");
        }

        var alreadyRegistered = await _db.ContactNumbers.AnyAsync(
            x => x.BuyerId == buyerId && x.E164Number == validation.CanonicalNumber && x.DeletedAt == null,
            cancellationToken);
        if (alreadyRegistered)
        {
            throw new OrderNotificationApiException(409, "That contact number is already registered.");
        }

        var contact = new ContactNumber(buyerId, validation.CanonicalNumber, UtcNow());
        _db.ContactNumbers.Add(contact);
        await _db.SaveChangesAsync(cancellationToken);
        return new RegisterContactNumberResponse(contact.Id, contact.E164Number);
    }

    public async Task<IReadOnlyList<ContactNumberResponse>> GetContactNumbersAsync(
        string buyerId,
        CancellationToken cancellationToken) =>
        await _db.ContactNumbers
            .AsNoTracking()
            .Where(x => x.BuyerId == buyerId && x.DeletedAt == null)
            .OrderBy(x => x.CreatedAt)
            .Select(x => new ContactNumberResponse(x.Id, x.E164Number, x.CreatedAt))
            .ToListAsync(cancellationToken);

    public async Task RemoveContactNumberAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken)
    {
        var contact = await _db.ContactNumbers.SingleOrDefaultAsync(
            x => x.Id == contactNumberId && x.BuyerId == buyerId && x.DeletedAt == null,
            cancellationToken);
        if (contact is null)
        {
            throw NotFound();
        }

        contact.Remove(UtcNow());
        await _db.SaveChangesAsync(cancellationToken);
        await CancelScheduledNotificationsAsync(contact.Id, null, cancellationToken);
    }

    public async Task<PlaceOrderResponse> PlaceOrderAsync(
        string buyerId,
        PlaceOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Items.Count == 0 || request.Items.Any(x => x.CatalogItemId <= 0 || x.Quantity <= 0))
        {
            throw BadRequest("At least one catalog item with a positive quantity is required.");
        }

        if (request.Items.Select(x => x.CatalogItemId).Distinct().Count() != request.Items.Count)
        {
            throw BadRequest("Each catalogItemId may appear only once.");
        }

        var itemIds = request.Items.Select(x => x.CatalogItemId).ToArray();
        var catalogItems = await _db.CatalogItems
            .AsNoTracking()
            .Where(x => itemIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        if (catalogItems.Count != itemIds.Length)
        {
            throw BadRequest("One or more catalog items do not exist.");
        }

        var orderItems = request.Items.Select(line =>
        {
            var catalogItem = catalogItems[line.CatalogItemId];
            return new OrderItem(
                new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri),
                catalogItem.Price,
                line.Quantity);
        }).ToList();

        var address = MapAddress(request.ShippingAddress);
        var order = new Order(buyerId, address, orderItems);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);

        var body = $"eShopOnWeb: Order {order.Id} was placed. Total: {order.Total().ToString("0.00", CultureInfo.InvariantCulture)}.";
        await SendToActiveContactsAsync(order, NotificationType.OrderPlaced, body, null, cancellationToken);
        return new PlaceOrderResponse(order.Id);
    }

    public async Task DispatchOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        if (order is null)
        {
            throw NotFound();
        }

        if (order.Status != OrderStatus.Placed)
        {
            throw new OrderNotificationApiException(409, "Only a placed order can be dispatched.");
        }

        var now = UtcNow();
        order.Dispatch(now);
        await _db.SaveChangesAsync(cancellationToken);

        await SendToActiveContactsAsync(
            order,
            NotificationType.OrderDispatched,
            $"eShopOnWeb: Order {order.Id} has been dispatched and is on its way.",
            null,
            cancellationToken);

        await SendToActiveContactsAsync(
            order,
            NotificationType.DeliveryFollowUp,
            $"eShopOnWeb: How did delivery of order {order.Id} go? We hope you are enjoying it.",
            now.AddDays(3),
            cancellationToken);
    }

    public async Task CancelOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        if (order is null)
        {
            throw NotFound();
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            throw new OrderNotificationApiException(409, "The order is already cancelled.");
        }

        order.Cancel(UtcNow());
        await _db.SaveChangesAsync(cancellationToken);

        await CancelScheduledNotificationsAsync(null, order.Id, cancellationToken);
        await SendToActiveContactsAsync(
            order,
            NotificationType.OrderCancelled,
            $"eShopOnWeb: Order {order.Id} has been cancelled.",
            null,
            cancellationToken);
    }

    public async Task<MyOrdersResponse> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        var orders = await _db.Orders
            .AsNoTracking()
            .Include(x => x.OrderItems)
            .Where(x => x.BuyerId == buyerId)
            .OrderByDescending(x => x.OrderDate)
            .ToListAsync(cancellationToken);
        var orderIds = orders.Select(x => x.Id).ToArray();
        var notifications = await RefreshNotificationsAsync(
            _db.OrderNotifications.Where(x => x.BuyerId == buyerId && orderIds.Contains(x.OrderId)),
            cancellationToken);
        var byOrder = notifications.GroupBy(x => x.OrderId).ToDictionary(x => x.Key, x => x.ToList());

        return new MyOrdersResponse(orders.Select(order => MapOrder(
            order,
            byOrder.GetValueOrDefault(order.Id) ?? new List<OrderNotification>())).ToList());
    }

    public async Task<OrderNotificationsResponse> GetOrderNotificationsAsync(
        string buyerId,
        int orderId,
        CancellationToken cancellationToken)
    {
        var ownsOrder = await _db.Orders.AsNoTracking().AnyAsync(
            x => x.Id == orderId && x.BuyerId == buyerId,
            cancellationToken);
        if (!ownsOrder)
        {
            throw NotFound();
        }

        var notifications = await RefreshNotificationsAsync(
            _db.OrderNotifications.Where(x => x.OrderId == orderId && x.BuyerId == buyerId),
            cancellationToken);
        return new OrderNotificationsResponse(orderId, notifications.Select(MapNotification).ToList());
    }

    public async Task<ResendNotificationResponse> ResendAsync(
        int notificationId,
        ResendNotificationRequest request,
        CancellationToken cancellationToken)
    {
        var key = request.IdempotencyKey?.Trim();
        if (string.IsNullOrWhiteSpace(key) || key.Length > 128)
        {
            throw BadRequest("An idempotencyKey of at most 128 characters is required.");
        }

        var lockKey = $"{notificationId}:{key}";
        var gate = ResendLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await ResendCoreAsync(notificationId, key, cancellationToken);
        }
        finally
        {
            gate.Release();
            ResendLocks.TryRemove(lockKey, out _);
        }
    }

    private async Task<ResendNotificationResponse> ResendCoreAsync(
        int notificationId,
        string key,
        CancellationToken cancellationToken)
    {

        var priorAttempt = await _db.OrderNotifications.AsNoTracking().SingleOrDefaultAsync(
            x => x.ResendOfNotificationId == notificationId && x.IdempotencyKey == key,
            cancellationToken);
        if (priorAttempt is not null)
        {
            return new ResendNotificationResponse(priorAttempt.Id);
        }

        var source = await _db.OrderNotifications.SingleOrDefaultAsync(x => x.Id == notificationId, cancellationToken);
        if (source is null)
        {
            throw NotFound();
        }

        await RefreshNotificationAsync(source, cancellationToken);
        if (!ResendableStatuses.Contains(source.ProviderStatus))
        {
            throw new OrderNotificationApiException(409, "Only a failed or undelivered notification can be resent.");
        }

        if (string.IsNullOrEmpty(source.Body))
        {
            throw new OrderNotificationApiException(409, "Disposed notification content cannot be resent.");
        }

        var orderCancelled = await _db.Orders.AsNoTracking().AnyAsync(
            x => x.Id == source.OrderId && x.Status == OrderStatus.Cancelled,
            cancellationToken);
        if (orderCancelled && source.Type == NotificationType.DeliveryFollowUp)
        {
            throw new OrderNotificationApiException(409, "A delivery follow-up for a cancelled order cannot be resent.");
        }

        var contact = await _db.ContactNumbers.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == source.ContactNumberId && x.DeletedAt == null,
            cancellationToken);
        if (contact is null)
        {
            throw new OrderNotificationApiException(409, "The destination is no longer registered.");
        }

        var resend = new OrderNotification(
            source.OrderId,
            source.ContactNumberId,
            source.BuyerId,
            NotificationType.Resend,
            source.Body,
            UtcNow(),
            resendOfNotificationId: source.Id,
            idempotencyKey: key);
        _db.OrderNotifications.Add(resend);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _db.Entry(resend).State = EntityState.Detached;
            var concurrentAttempt = await _db.OrderNotifications.AsNoTracking().SingleOrDefaultAsync(
                x => x.ResendOfNotificationId == notificationId && x.IdempotencyKey == key,
                cancellationToken);
            if (concurrentAttempt is not null)
            {
                return new ResendNotificationResponse(concurrentAttempt.Id);
            }

            throw;
        }
        await SendNotificationAsync(resend, contact.E164Number, cancellationToken);
        return new ResendNotificationResponse(resend.Id);
    }

    public async Task DisposeContentAsync(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _db.OrderNotifications.SingleOrDefaultAsync(x => x.Id == notificationId, cancellationToken);
        if (notification is null)
        {
            throw NotFound();
        }

        if (notification.ContentRedactedAt.HasValue)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(notification.ProviderMessageSid))
        {
            try
            {
                var providerMessage = await _provider.RedactContentAsync(notification.ProviderMessageSid, cancellationToken);
                notification.RecordProviderState(providerMessage, UtcNow());
            }
            catch (TextMessageProviderException)
            {
                throw new OrderNotificationApiException(502, "The notification content could not be disposed at the provider.");
            }
        }

        notification.RedactContent(UtcNow());
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<ReconciliationResponse> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (from >= to)
        {
            throw BadRequest("from must be earlier than to.");
        }

        IReadOnlyList<ProviderMessage> providerMessages;
        try
        {
            providerMessages = await _provider.ListAsync(from, to, cancellationToken);
        }
        catch (TextMessageProviderException)
        {
            throw new OrderNotificationApiException(502, "The provider reconciliation data could not be retrieved.");
        }

        var local = await _db.OrderNotifications.AsNoTracking()
            .Where(x => (x.ProviderSentAt >= from && x.ProviderSentAt <= to) ||
                        (x.ProviderSentAt == null &&
                         x.ProviderStatus != "scheduled" &&
                         x.ProviderStatus != "canceled" &&
                         x.CreatedAt >= from && x.CreatedAt <= to))
            .ToListAsync(cancellationToken);
        var localBySid = local
            .Where(x => x.ProviderMessageSid != null)
            .ToDictionary(x => x.ProviderMessageSid!, StringComparer.Ordinal);
        var entries = new List<ReconciliationEntryResponse>();

        foreach (var providerMessage in providerMessages)
        {
            localBySid.TryGetValue(providerMessage.Sid, out var notification);
            entries.Add(new ReconciliationEntryResponse(
                notification is null ? "providerOnly" : "matched",
                providerMessage.Sid,
                notification?.Id,
                notification?.OrderId,
                providerMessage.Status,
                notification?.ProviderStatus,
                providerMessage.ErrorCode,
                providerMessage.DateSent,
                notification?.CreatedAt,
                Mask(providerMessage.To)));
        }

        var providerSids = providerMessages.Select(x => x.Sid).ToHashSet(StringComparer.Ordinal);
        entries.AddRange(local
            .Where(x => x.ProviderMessageSid is null || !providerSids.Contains(x.ProviderMessageSid))
            .Select(x => new ReconciliationEntryResponse(
                "applicationOnly",
                x.ProviderMessageSid,
                x.Id,
                x.OrderId,
                null,
                x.ProviderStatus,
                x.ProviderErrorCode,
                null,
                x.CreatedAt,
                null)));

        return new ReconciliationResponse(from, to, entries
            .OrderBy(x => x.ProviderDateSent ?? x.ApplicationCreatedAt)
            .ToList());
    }

    private async Task SendToActiveContactsAsync(
        Order order,
        NotificationType type,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        var contacts = await _db.ContactNumbers
            .AsNoTracking()
            .Where(x => x.BuyerId == order.BuyerId && x.DeletedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var contact in contacts)
        {
            var notification = new OrderNotification(
                order.Id,
                contact.Id,
                order.BuyerId,
                type,
                body,
                UtcNow(),
                sendAt);
            _db.OrderNotifications.Add(notification);
            await _db.SaveChangesAsync(cancellationToken);
            await SendNotificationAsync(notification, contact.E164Number, cancellationToken);
        }
    }

    private async Task SendNotificationAsync(
        OrderNotification notification,
        string destination,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _provider.SendAsync(destination, notification.Body!, notification.ScheduledFor, cancellationToken);
            notification.RecordProviderState(result, UtcNow());
        }
        catch (TextMessageProviderException ex)
        {
            notification.RecordProviderFailure(ex.ProviderErrorCode, UtcNow());
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task CancelScheduledNotificationsAsync(
        int? contactNumberId,
        int? orderId,
        CancellationToken cancellationToken)
    {
        var query = _db.OrderNotifications.Where(x =>
            x.Type == NotificationType.DeliveryFollowUp &&
            x.ProviderMessageSid != null &&
            x.ProviderStatus != "canceled");
        if (contactNumberId.HasValue)
        {
            query = query.Where(x => x.ContactNumberId == contactNumberId.Value);
        }
        if (orderId.HasValue)
        {
            query = query.Where(x => x.OrderId == orderId.Value);
        }

        var notifications = await query.ToListAsync(cancellationToken);
        var requestedAt = UtcNow();
        foreach (var notification in notifications)
        {
            notification.RequestCancellation(requestedAt);
        }
        await _db.SaveChangesAsync(cancellationToken);

        foreach (var notification in notifications)
        {
            try
            {
                var current = await _provider.GetAsync(notification.ProviderMessageSid!, cancellationToken);
                notification.RecordProviderState(current, UtcNow());
                if (string.Equals(current.Status, "scheduled", StringComparison.OrdinalIgnoreCase))
                {
                    var cancelled = await _provider.CancelAsync(notification.ProviderMessageSid!, cancellationToken);
                    notification.RecordProviderState(cancelled, UtcNow());
                }
                else
                {
                    notification.CompleteCancellationAttempt(UtcNow());
                }
            }
            catch (TextMessageProviderException)
            {
                // The commerce/contact operation remains successful; the last known provider state is retained.
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<List<OrderNotification>> RefreshNotificationsAsync(
        IQueryable<OrderNotification> query,
        CancellationToken cancellationToken)
    {
        var notifications = await query.OrderBy(x => x.CreatedAt).ToListAsync(cancellationToken);
        foreach (var notification in notifications)
        {
            await RefreshNotificationAsync(notification, cancellationToken);
        }
        await _db.SaveChangesAsync(cancellationToken);
        return notifications;
    }

    private async Task RefreshNotificationAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(notification.ProviderMessageSid))
        {
            return;
        }

        try
        {
            var current = await _provider.GetAsync(notification.ProviderMessageSid, cancellationToken);
            notification.RecordProviderState(current, UtcNow());
        }
        catch (TextMessageProviderException)
        {
            // A transient read failure must not replace the last delivery state.
        }
    }

    private static Address MapAddress(ShippingAddressRequest? address)
    {
        if (address is null)
        {
            return new Address("Not provided", "Not provided", string.Empty, "Not provided", "Not provided");
        }

        if (string.IsNullOrWhiteSpace(address.Street) || string.IsNullOrWhiteSpace(address.City) ||
            string.IsNullOrWhiteSpace(address.Country) || string.IsNullOrWhiteSpace(address.ZipCode))
        {
            throw BadRequest("A supplied shippingAddress requires street, city, country and zipCode.");
        }

        return new Address(address.Street, address.City, address.State, address.Country, address.ZipCode);
    }

    private static OrderResponse MapOrder(Order order, IReadOnlyList<OrderNotification> notifications) => new(
        order.Id,
        order.Status.ToString(),
        order.OrderDate,
        order.DispatchedAt,
        order.CancelledAt,
        order.Total(),
        order.OrderItems.Select(x => new OrderItemResponse(
            x.ItemOrdered.CatalogItemId,
            x.ItemOrdered.ProductName,
            x.UnitPrice,
            x.Units)).ToList(),
        notifications.Select(MapNotification).ToList());

    private static NotificationResponse MapNotification(OrderNotification notification) => new(
        notification.Id,
        notification.Type.ToString(),
        notification.ProviderStatus,
        notification.ProviderMessageSid,
        notification.ProviderErrorCode,
        notification.Body,
        notification.ContentRedactedAt.HasValue,
        notification.CreatedAt,
        notification.ScheduledFor,
        notification.ProviderSentAt,
        notification.ResendOfNotificationId);

    private static string? Mask(string? destination)
    {
        if (string.IsNullOrEmpty(destination))
        {
            return null;
        }

        var visible = Math.Min(4, destination.Length);
        return new string('*', destination.Length - visible) + destination[^visible..];
    }

    private DateTimeOffset UtcNow() => _timeProvider.GetUtcNow();
    private static OrderNotificationApiException BadRequest(string message) => new(400, message);
    private static OrderNotificationApiException NotFound() => new(404, "The requested resource was not found.");
}
