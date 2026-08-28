using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public sealed class OrderNotificationService : IOrderNotificationService
{
    private static readonly SemaphoreSlim[] ResendLocks = CreateLockStripes();
    private static readonly SemaphoreSlim[] OrderLocks = CreateLockStripes();
    private static readonly SemaphoreSlim[] ContactLocks = CreateLockStripes();
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);
    private readonly CatalogContext _context;
    private readonly ISmsProvider _provider;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        CatalogContext context,
        ISmsProvider provider,
        TimeProvider timeProvider,
        ILogger<OrderNotificationService> logger)
    {
        _context = context;
        _provider = provider;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<ContactNumberView> RegisterContactNumberAsync(
        string buyerId,
        string number,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(number))
        {
            throw new RequestValidationException("A mobile number is required.");
        }

        var validation = await _provider.ValidateDestinationAsync(number.Trim(), cancellationToken);
        if (!validation.IsValid || string.IsNullOrWhiteSpace(validation.CanonicalNumber))
        {
            throw new RequestValidationException("Twilio does not consider that number a valid destination.");
        }

        var existing = await _context.ContactNumbers
            .SingleOrDefaultAsync(
                x => x.BuyerId == buyerId && x.CanonicalNumber == validation.CanonicalNumber && x.RemovedAt == null,
                cancellationToken);

        if (existing is not null)
        {
            throw new ResourceConflictException("That contact number is already registered.");
        }

        var now = UtcNow();
        var contact = new ContactNumber(buyerId, validation.CanonicalNumber, now);
        _context.ContactNumbers.Add(contact);
        await _context.SaveChangesAsync(cancellationToken);
        return ToView(contact);
    }

    public async Task<IReadOnlyList<ContactNumberView>> GetContactNumbersAsync(string buyerId, CancellationToken cancellationToken) =>
        await _context.ContactNumbers
            .AsNoTracking()
            .Where(x => x.BuyerId == buyerId && x.RemovedAt == null)
            .OrderBy(x => x.Id)
            .Select(x => new ContactNumberView(x.Id, x.CanonicalNumber, x.CreatedAt))
            .ToListAsync(cancellationToken);

    public async Task RemoveContactNumberAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken)
    {
        var contactLock = LockFor(ContactLocks, contactNumberId.ToString());
        await contactLock.WaitAsync(cancellationToken);
        try
        {
            var contact = await _context.ContactNumbers
                .SingleOrDefaultAsync(x => x.Id == contactNumberId && x.BuyerId == buyerId && x.RemovedAt == null, cancellationToken)
                ?? throw new ResourceNotFoundException("Contact number not found.");

            contact.Remove(UtcNow());
            await _context.SaveChangesAsync(cancellationToken);
            await CancelScheduledNotificationsAsync(contactNumberId, null, cancellationToken);
        }
        finally
        {
            contactLock.Release();
        }
    }

    public async Task<int> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<OrderLineInput> items,
        ShippingAddressInput? address,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            throw new RequestValidationException("At least one catalog item is required.");
        }

        if (items.Any(x => x.CatalogItemId <= 0 || x.Quantity <= 0))
        {
            throw new RequestValidationException("Catalog item ids and quantities must be positive.");
        }

        if (address is not null &&
            (string.IsNullOrWhiteSpace(address.Street) ||
             string.IsNullOrWhiteSpace(address.City) ||
             string.IsNullOrWhiteSpace(address.Country) ||
             string.IsNullOrWhiteSpace(address.ZipCode)))
        {
            throw new RequestValidationException("Street, city, country, and zip code are required when a shipping address is supplied.");
        }

        var lines = items
            .GroupBy(x => x.CatalogItemId)
            .Select(x => new OrderLineInput(x.Key, checked(x.Sum(y => y.Quantity))))
            .ToList();
        var ids = lines.Select(x => x.CatalogItemId).ToList();
        var catalogItems = await _context.CatalogItems
            .AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .ToListAsync(cancellationToken);

        if (catalogItems.Count != ids.Count)
        {
            throw new RequestValidationException("One or more catalog items do not exist.");
        }

        var orderItems = lines.Select(line =>
        {
            var item = catalogItems.Single(x => x.Id == line.CatalogItemId);
            return new OrderItem(
                new CatalogItemOrdered(item.Id, item.Name, item.PictureUri),
                item.Price,
                line.Quantity);
        }).ToList();

        var shippingAddress = address is null
            ? new Address("Not supplied through PublicApi", "Not supplied", string.Empty, "Not supplied", "Not supplied")
            : new Address(address.Street, address.City, address.State, address.Country, address.ZipCode);
        var order = new Order(buyerId, shippingAddress, orderItems);
        _context.Orders.Add(order);
        await _context.SaveChangesAsync(cancellationToken);

        await NotifyActiveContactsAsync(order, NotificationType.OrderPlaced, MessageBody(NotificationType.OrderPlaced, order.Id), null, cancellationToken);
        return order.Id;
    }

    public async Task DispatchOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var orderLock = LockFor(OrderLocks, orderId.ToString());
        await orderLock.WaitAsync(cancellationToken);
        try
        {
            var order = await _context.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken)
                ?? throw new ResourceNotFoundException("Order not found.");

            if (order.Status == OrderStatus.Cancelled)
            {
                throw new ResourceConflictException("A cancelled order cannot be dispatched.");
            }

            if (!order.Dispatch()) return;
            await _context.SaveChangesAsync(cancellationToken);

            await NotifyActiveContactsAsync(
                order,
                NotificationType.OrderDispatched,
                MessageBody(NotificationType.OrderDispatched, order.Id),
                null,
                cancellationToken);

            await NotifyActiveContactsAsync(
                order,
                NotificationType.DeliveryFollowUp,
                MessageBody(NotificationType.DeliveryFollowUp, order.Id),
                UtcNow().Add(FollowUpDelay),
                cancellationToken);
        }
        finally
        {
            orderLock.Release();
        }
    }

    public async Task CancelOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var orderLock = LockFor(OrderLocks, orderId.ToString());
        await orderLock.WaitAsync(cancellationToken);
        try
        {
            var order = await _context.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken)
                ?? throw new ResourceNotFoundException("Order not found.");

            if (!order.Cancel()) return;
            await _context.SaveChangesAsync(cancellationToken);

            await CancelScheduledNotificationsAsync(null, order.Id, cancellationToken);
            await NotifyActiveContactsAsync(
                order,
                NotificationType.OrderCancelled,
                MessageBody(NotificationType.OrderCancelled, order.Id),
                null,
                cancellationToken);
        }
        finally
        {
            orderLock.Release();
        }
    }

    public async Task<IReadOnlyList<OrderView>> GetOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        var orders = await _context.Orders
            .AsNoTracking()
            .Include(x => x.OrderItems)
            .Where(x => x.BuyerId == buyerId)
            .OrderByDescending(x => x.OrderDate)
            .ToListAsync(cancellationToken);
        var orderIds = orders.Select(x => x.Id).ToList();
        var notifications = await _context.OrderNotifications
            .Where(x => orderIds.Contains(x.OrderId))
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        await RefreshProviderStatesAsync(notifications, cancellationToken);

        return orders.Select(order => new OrderView(
            order.Id,
            order.OrderDate,
            order.Status.ToString(),
            order.Total(),
            order.OrderItems.Select(item => new OrderItemView(
                item.ItemOrdered.CatalogItemId,
                item.ItemOrdered.ProductName,
                item.UnitPrice,
                item.Units)).ToList(),
            notifications.Where(x => x.OrderId == order.Id).Select(ToView).ToList())).ToList();
    }

    public async Task<IReadOnlyList<NotificationView>> GetOrderNotificationsAsync(
        string buyerId,
        int orderId,
        CancellationToken cancellationToken)
    {
        var ownsOrder = await _context.Orders.AsNoTracking().AnyAsync(x => x.Id == orderId && x.BuyerId == buyerId, cancellationToken);
        if (!ownsOrder)
        {
            throw new ResourceNotFoundException("Order not found.");
        }

        var notifications = await _context.OrderNotifications
            .Where(x => x.OrderId == orderId)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
        await RefreshProviderStatesAsync(notifications, cancellationToken);
        return notifications.Select(ToView).ToList();
    }

    public async Task<int> ResendNotificationAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 200)
        {
            throw new RequestValidationException("An idempotency key between 1 and 200 characters is required.");
        }

        var keyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(idempotencyKey)));
        var lockKey = $"{notificationId}:{keyHash}";
        var resendLock = LockFor(ResendLocks, lockKey);
        await resendLock.WaitAsync(cancellationToken);
        try
        {
            var existing = await _context.NotificationResends
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.SourceNotificationId == notificationId && x.IdempotencyKeyHash == keyHash, cancellationToken);
            if (existing is not null) return existing.NotificationId;

            var source = await _context.OrderNotifications.SingleOrDefaultAsync(x => x.Id == notificationId, cancellationToken)
                ?? throw new ResourceNotFoundException("Notification not found.");
            var order = await _context.Orders.AsNoTracking().SingleAsync(x => x.Id == source.OrderId, cancellationToken);
            var contact = await _context.ContactNumbers.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == source.ContactNumberId && x.RemovedAt == null, cancellationToken)
                ?? throw new ResourceConflictException("The destination is no longer registered.");

            if (source.Type == NotificationType.DeliveryFollowUp && order.Status == OrderStatus.Cancelled)
            {
                throw new ResourceConflictException("A delivery follow-up cannot be resent for a cancelled order.");
            }

            if (source.Body is null)
            {
                throw new ResourceConflictException("Disposed message content cannot be resent.");
            }

            if (source.ProviderMessageSid is not null)
            {
                var providerState = await _provider.GetMessageAsync(source.ProviderMessageSid, cancellationToken);
                source.ApplyProviderState(providerState, UtcNow());
                await _context.SaveChangesAsync(cancellationToken);
            }

            if (!CanResend(source.DeliveryStatus))
            {
                throw new ResourceConflictException("Only a notification with a terminal non-delivery outcome can be resent.");
            }

            var contactLock = LockFor(ContactLocks, contact.Id.ToString());
            await contactLock.WaitAsync(cancellationToken);
            try
            {
                var contactStillActive = await _context.ContactNumbers.AsNoTracking()
                    .AnyAsync(x => x.Id == contact.Id && x.RemovedAt == null, cancellationToken);
                if (!contactStillActive)
                {
                    throw new ResourceConflictException("The destination is no longer registered.");
                }

                var resend = new OrderNotification(
                    source.OrderId,
                    contact.Id,
                    source.Type,
                    source.Body,
                    UtcNow(),
                    sourceNotificationId: source.Id);
                _context.OrderNotifications.Add(resend);
                await _context.SaveChangesAsync(cancellationToken);

                _context.NotificationResends.Add(new NotificationResend(source.Id, keyHash, resend.Id, UtcNow()));
                await _context.SaveChangesAsync(cancellationToken);
                await SendExistingNotificationAsync(resend, contact.CanonicalNumber, cancellationToken);
                return resend.Id;
            }
            finally
            {
                contactLock.Release();
            }
        }
        finally
        {
            resendLock.Release();
        }
    }

    public async Task DisposeNotificationContentAsync(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _context.OrderNotifications.SingleOrDefaultAsync(x => x.Id == notificationId, cancellationToken)
            ?? throw new ResourceNotFoundException("Notification not found.");

        if (notification.ContentDisposedAt is not null) return;

        if (notification.ProviderMessageSid is not null)
        {
            var providerState = await _provider.RedactMessageAsync(notification.ProviderMessageSid, cancellationToken);
            notification.ApplyProviderState(providerState, UtcNow());
        }

        notification.DisposeContent(UtcNow());
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<ReconciliationView> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        if (from >= to)
        {
            throw new RequestValidationException("The 'from' date-time must be earlier than 'to'.");
        }

        var providerMessages = await _provider.ListMessagesAsync(from, to, cancellationToken);
        var providerSids = providerMessages.Select(x => x.Sid).ToHashSet(StringComparer.Ordinal);
        var local = await _context.OrderNotifications
            .AsNoTracking()
            .Where(x => (x.CreatedAt >= from && x.CreatedAt <= to) ||
                        (x.ProviderDateSent != null && x.ProviderDateSent >= from && x.ProviderDateSent <= to))
            .ToListAsync(cancellationToken);

        foreach (var sidChunk in providerSids.Chunk(500))
        {
            var chunk = sidChunk.ToList();
            var matches = await _context.OrderNotifications.AsNoTracking()
                .Where(x => x.ProviderMessageSid != null && chunk.Contains(x.ProviderMessageSid))
                .ToListAsync(cancellationToken);
            local.AddRange(matches.Where(x => local.All(y => y.Id != x.Id)));
        }

        var bySid = local.Where(x => x.ProviderMessageSid is not null)
            .ToDictionary(x => x.ProviderMessageSid!, StringComparer.Ordinal);
        var entries = new List<ReconciliationEntry>();

        foreach (var providerMessage in providerMessages)
        {
            bySid.TryGetValue(providerMessage.Sid, out var notification);
            entries.Add(new ReconciliationEntry(
                notification is null ? "provider-only" : "matched",
                providerMessage.Sid,
                notification?.Id,
                notification?.OrderId,
                notification?.Type.ToString(),
                providerMessage.Status,
                providerMessage.To,
                providerMessage.DateCreated,
                providerMessage.DateSent,
                notification?.CreatedAt));
        }

        entries.AddRange(local
            .Where(x => x.ProviderMessageSid is null || !providerSids.Contains(x.ProviderMessageSid))
            .Select(x => new ReconciliationEntry(
                "application-only",
                x.ProviderMessageSid,
                x.Id,
                x.OrderId,
                x.Type.ToString(),
                x.DeliveryStatus,
                null,
                x.ProviderDateCreated,
                x.ProviderDateSent,
                x.CreatedAt)));

        return new ReconciliationView(
            from,
            to,
            entries.Count(x => x.ReconciliationStatus == "matched"),
            entries.Count(x => x.ReconciliationStatus == "provider-only"),
            entries.Count(x => x.ReconciliationStatus == "application-only"),
            entries.OrderBy(x => x.ProviderDateCreated ?? x.ApplicationCreatedAt).ToList());
    }

    public async Task RetryPendingCancellationsAsync(CancellationToken cancellationToken)
    {
        var pending = await _context.OrderNotifications
            .Where(x => x.ProviderCancellationPending && x.ProviderMessageSid != null)
            .ToListAsync(cancellationToken);

        foreach (var notification in pending)
        {
            await TryCancelProviderMessageAsync(notification, cancellationToken);
        }
    }

    private async Task NotifyActiveContactsAsync(
        Order order,
        NotificationType type,
        string body,
        DateTimeOffset? scheduledAt,
        CancellationToken cancellationToken)
    {
        var contacts = await _context.ContactNumbers
            .AsNoTracking()
            .Where(x => x.BuyerId == order.BuyerId && x.RemovedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var contact in contacts)
        {
            var contactLock = LockFor(ContactLocks, contact.Id.ToString());
            await contactLock.WaitAsync(cancellationToken);
            try
            {
                var contactStillActive = await _context.ContactNumbers.AsNoTracking()
                    .AnyAsync(x => x.Id == contact.Id && x.RemovedAt == null, cancellationToken);
                if (!contactStillActive) continue;

                var notification = new OrderNotification(order.Id, contact.Id, type, body, UtcNow(), scheduledAt);
                _context.OrderNotifications.Add(notification);
                await _context.SaveChangesAsync(cancellationToken);
                await SendExistingNotificationAsync(notification, contact.CanonicalNumber, cancellationToken);
            }
            finally
            {
                contactLock.Release();
            }
        }
    }

    private async Task SendExistingNotificationAsync(
        OrderNotification notification,
        string canonicalNumber,
        CancellationToken cancellationToken)
    {
        try
        {
            var providerMessage = await _provider.SendAsync(canonicalNumber, notification.Body!, notification.ScheduledAt, cancellationToken);
            notification.ApplyProviderState(providerMessage, UtcNow());
        }
        catch (SmsProviderException ex)
        {
            notification.MarkProviderFailure(ex.ProviderErrorCode, UtcNow());
            _logger.LogWarning(
                "SMS provider request failed for notification {NotificationId} on order {OrderId}; provider code {ProviderCode}.",
                notification.Id,
                notification.OrderId,
                ex.ProviderErrorCode);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task RefreshProviderStatesAsync(List<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        var changed = false;
        foreach (var notification in notifications.Where(x => x.ProviderMessageSid is not null))
        {
            try
            {
                var state = await _provider.GetMessageAsync(notification.ProviderMessageSid!, cancellationToken);
                notification.ApplyProviderState(state, UtcNow());
                changed = true;
            }
            catch (SmsProviderException ex)
            {
                _logger.LogWarning(
                    "SMS status refresh failed for notification {NotificationId}; provider code {ProviderCode}.",
                    notification.Id,
                    ex.ProviderErrorCode);
            }
        }

        if (changed) await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task CancelScheduledNotificationsAsync(
        int? contactNumberId,
        int? orderId,
        CancellationToken cancellationToken)
    {
        var scheduled = await _context.OrderNotifications
            .Where(x => x.Type == NotificationType.DeliveryFollowUp &&
                        x.ProviderMessageSid != null &&
                        x.DeliveryStatus == "scheduled" &&
                        (contactNumberId == null || x.ContactNumberId == contactNumberId) &&
                        (orderId == null || x.OrderId == orderId))
            .ToListAsync(cancellationToken);

        foreach (var notification in scheduled)
        {
            notification.RequestProviderCancellation();
        }

        await _context.SaveChangesAsync(cancellationToken);
        foreach (var notification in scheduled)
        {
            await TryCancelProviderMessageAsync(notification, cancellationToken);
        }
    }

    private async Task TryCancelProviderMessageAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            var state = await _provider.CancelScheduledMessageAsync(notification.ProviderMessageSid!, cancellationToken);
            notification.ApplyProviderState(state, UtcNow());
        }
        catch (SmsProviderException ex)
        {
            _logger.LogWarning(
                "Scheduled SMS cancellation remains pending for notification {NotificationId}; provider code {ProviderCode}.",
                notification.Id,
                ex.ProviderErrorCode);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    private static bool CanResend(string status) => status is
        "failed" or "undelivered" or "canceled" or NotificationDeliveryStatuses.ProviderRequestFailed;

    private static string MessageBody(NotificationType type, int orderId) => type switch
    {
        NotificationType.OrderPlaced => $"eShopOnWeb: Order #{orderId} has been placed.",
        NotificationType.OrderDispatched => $"eShopOnWeb: Order #{orderId} is on its way.",
        NotificationType.DeliveryFollowUp => $"eShopOnWeb: How did delivery of order #{orderId} go?",
        NotificationType.OrderCancelled => $"eShopOnWeb: Order #{orderId} has been cancelled.",
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    private DateTimeOffset UtcNow() => _timeProvider.GetUtcNow();

    private static SemaphoreSlim[] CreateLockStripes() =>
        Enumerable.Range(0, 256).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    private static SemaphoreSlim LockFor(SemaphoreSlim[] locks, string key) =>
        locks[(int)((uint)StringComparer.Ordinal.GetHashCode(key) % (uint)locks.Length)];

    private static ContactNumberView ToView(ContactNumber contact) =>
        new(contact.Id, contact.CanonicalNumber, contact.CreatedAt);

    private static NotificationView ToView(OrderNotification notification) => new(
        notification.Id,
        notification.OrderId,
        notification.Type.ToString(),
        notification.DeliveryStatus,
        notification.ProviderMessageSid,
        notification.ProviderErrorCode,
        notification.Body,
        notification.ContentDisposedAt is not null,
        notification.CreatedAt,
        notification.ScheduledAt,
        notification.ProviderDateSent,
        notification.LastProviderCheckAt,
        notification.SourceNotificationId,
        notification.ProviderCancellationPending);
}
