using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

public sealed class OrderNotificationService
{
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);
    private readonly CatalogContext _db;
    private readonly ITwilioLookupClient _lookup;
    private readonly ITwilioMessagingClient _messaging;
    private readonly TimeProvider _timeProvider;
    private readonly NotificationIdempotencyCoordinator _idempotency;

    public OrderNotificationService(
        CatalogContext db,
        ITwilioLookupClient lookup,
        ITwilioMessagingClient messaging,
        TimeProvider timeProvider,
        NotificationIdempotencyCoordinator idempotency)
    {
        _db = db;
        _lookup = lookup;
        _messaging = messaging;
        _timeProvider = timeProvider;
        _idempotency = idempotency;
    }

    public async Task<OperationResult<ContactNumber>> RegisterContactNumberAsync(
        string buyerId,
        string suppliedNumber,
        CancellationToken cancellationToken)
    {
        TwilioLookupResponse lookup;
        try
        {
            lookup = await _lookup.LookupAsync(suppliedNumber, cancellationToken);
        }
        catch (Exception ex) when (IsProviderFailure(ex))
        {
            return OperationResult<ContactNumber>.Fail(
                OperationError.ProviderUnavailable,
                "The mobile number could not be validated with the messaging provider.");
        }

        if (!lookup.Valid || string.IsNullOrWhiteSpace(lookup.PhoneNumber))
        {
            return OperationResult<ContactNumber>.Fail(OperationError.Invalid, "The mobile number is not a valid destination.");
        }

        var exists = await _db.ContactNumbers.AnyAsync(
            x => x.BuyerId == buyerId && x.CanonicalNumber == lookup.PhoneNumber && x.DeletedAt == null,
            cancellationToken);
        if (exists)
        {
            return OperationResult<ContactNumber>.Fail(OperationError.Conflict, "That mobile number is already registered.");
        }

        var number = new ContactNumber(buyerId, lookup.PhoneNumber, UtcNow());
        _db.ContactNumbers.Add(number);
        await _db.SaveChangesAsync(cancellationToken);
        return OperationResult<ContactNumber>.Success(number);
    }

    public Task<List<ContactNumber>> GetContactNumbersAsync(string buyerId, CancellationToken cancellationToken) =>
        _db.ContactNumbers.AsNoTracking()
            .Where(x => x.BuyerId == buyerId && x.DeletedAt == null)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);

    public async Task<OperationResult<bool>> DeleteContactNumberAsync(
        string buyerId,
        int contactNumberId,
        CancellationToken cancellationToken)
    {
        var number = await _db.ContactNumbers.SingleOrDefaultAsync(
            x => x.Id == contactNumberId && x.BuyerId == buyerId && x.DeletedAt == null,
            cancellationToken);
        if (number is null)
        {
            return OperationResult<bool>.Fail(OperationError.NotFound, "Contact number not found.");
        }

        var scheduled = await _db.OrderNotifications
            .Where(x => x.ContactNumberId == number.Id && x.ScheduledFor != null && x.ProviderMessageSid != null)
            .ToListAsync(cancellationToken);
        foreach (var notification in scheduled.Where(x => x.NeedsCancellation))
        {
            if (!await TryCancelAsync(notification, cancellationToken))
            {
                await _db.SaveChangesAsync(cancellationToken);
                return OperationResult<bool>.Fail(
                    OperationError.ProviderUnavailable,
                    "A queued message could not be cancelled; the contact number remains registered.");
            }
        }

        number.Deactivate(UtcNow());
        await _db.SaveChangesAsync(cancellationToken);
        return OperationResult<bool>.Success(true);
    }

    public async Task<OperationResult<Order>> PlaceOrderAsync(
        string buyerId,
        PlaceOrderRequest request,
        CancellationToken cancellationToken)
    {
        var groupedLines = request.Items
            .GroupBy(x => x.CatalogItemId)
            .Select(x => new { CatalogItemId = x.Key, Quantity = x.Sum(y => y.Quantity) })
            .ToList();
        if (groupedLines.Any(x => x.Quantity is < 1 or > 1000))
        {
            return OperationResult<Order>.Fail(OperationError.Invalid, "Each catalog item quantity must be between 1 and 1000.");
        }

        var ids = groupedLines.Select(x => x.CatalogItemId).ToList();
        var catalogItems = await _db.CatalogItems.AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .ToListAsync(cancellationToken);
        if (catalogItems.Count != ids.Count)
        {
            return OperationResult<Order>.Fail(OperationError.Invalid, "One or more catalog items do not exist.");
        }

        var items = groupedLines.Select(line =>
        {
            var catalog = catalogItems.Single(x => x.Id == line.CatalogItemId);
            return new OrderItem(
                new CatalogItemOrdered(catalog.Id, catalog.Name, catalog.PictureUri),
                catalog.Price,
                line.Quantity);
        }).ToList();
        await using var transaction = await BeginPersistenceTransactionAsync(cancellationToken);
        var address = request.ShippingAddress;
        var order = new Order(
            buyerId,
            new Address(address.Street, address.City, address.State, address.Country, address.ZipCode),
            items);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);

        var notifications = await CreateNotificationsAsync(
            order.Id,
            NotificationType.OrderPlaced,
            $"eShopOnWeb: order {order.Id} was placed.",
            null,
            cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
        await SendAllAsync(notifications, cancellationToken);
        return OperationResult<Order>.Success(order);
    }

    public async Task<OperationResult<Order>> DispatchOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        if (order is null)
        {
            return OperationResult<Order>.Fail(OperationError.NotFound, "Order not found.");
        }

        await using var transaction = await BeginPersistenceTransactionAsync(cancellationToken);
        var now = UtcNow();
        if (!order.Dispatch(now))
        {
            return OperationResult<Order>.Fail(OperationError.Conflict, "Only a placed order can be dispatched.");
        }

        await _db.SaveChangesAsync(cancellationToken);
        var dispatched = await CreateNotificationsAsync(
            order.Id,
            NotificationType.OrderDispatched,
            $"eShopOnWeb: order {order.Id} is on its way.",
            null,
            cancellationToken);
        var followUp = await CreateNotificationsAsync(
            order.Id,
            NotificationType.DeliveryFollowUp,
            $"eShopOnWeb: how did delivery of order {order.Id} go?",
            now.Add(FollowUpDelay),
            cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
        await SendAllAsync(dispatched.Concat(followUp), cancellationToken);
        return OperationResult<Order>.Success(order);
    }

    public async Task<OperationResult<Order>> CancelOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        if (order is null)
        {
            return OperationResult<Order>.Fail(OperationError.NotFound, "Order not found.");
        }

        await using var transaction = await BeginPersistenceTransactionAsync(cancellationToken);
        if (!order.Cancel(UtcNow()))
        {
            return OperationResult<Order>.Fail(OperationError.Conflict, "The order is already cancelled.");
        }

        var followUps = await _db.OrderNotifications
            .Where(x => x.OrderId == order.Id && x.Type == NotificationType.DeliveryFollowUp)
            .ToListAsync(cancellationToken);
        foreach (var followUp in followUps.Where(x => x.NeedsCancellation))
        {
            followUp.RequestCancellation(UtcNow());
        }

        await _db.SaveChangesAsync(cancellationToken);
        var cancelled = await CreateNotificationsAsync(
            order.Id,
            NotificationType.OrderCancelled,
            $"eShopOnWeb: order {order.Id} was cancelled.",
            null,
            cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
        foreach (var followUp in followUps.Where(x => x.NeedsCancellation || x.ProviderStatus == "cancel_pending"))
        {
            await TryCancelAsync(followUp, cancellationToken);
        }
        await _db.SaveChangesAsync(cancellationToken);

        await SendAllAsync(cancelled, cancellationToken);
        return OperationResult<Order>.Success(order);
    }

    public async Task<List<MyOrderDto>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        var orders = await _db.Orders.AsNoTracking()
            .Where(x => x.BuyerId == buyerId)
            .Include(x => x.OrderItems)
            .OrderByDescending(x => x.OrderDate)
            .ToListAsync(cancellationToken);
        var orderIds = orders.Select(x => x.Id).ToList();
        var notifications = await _db.OrderNotifications.AsNoTracking()
            .Where(x => orderIds.Contains(x.OrderId))
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);

        return orders.Select(order => new MyOrderDto(
            order.Id,
            order.OrderDate,
            order.Status.ToString(),
            order.Total(),
            order.OrderItems.Select(x => new OrderLineDto(
                x.ItemOrdered.CatalogItemId, x.ItemOrdered.ProductName, x.UnitPrice, x.Units)).ToList(),
            notifications.Where(x => x.OrderId == order.Id).Select(ToSummary).ToList())).ToList();
    }

    public async Task<OperationResult<List<OrderNotification>>> GetOrderNotificationsAsync(
        string buyerId,
        int orderId,
        CancellationToken cancellationToken)
    {
        var owned = await _db.Orders.AsNoTracking()
            .AnyAsync(x => x.Id == orderId && x.BuyerId == buyerId, cancellationToken);
        if (!owned)
        {
            return OperationResult<List<OrderNotification>>.Fail(OperationError.NotFound, "Order not found.");
        }

        var notifications = await _db.OrderNotifications
            .Where(x => x.OrderId == orderId)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        foreach (var notification in notifications.Where(x => x.ProviderMessageSid != null && x.ContentDeletedAt == null))
        {
            await RefreshAsync(notification, cancellationToken);
        }
        await _db.SaveChangesAsync(cancellationToken);
        return OperationResult<List<OrderNotification>>.Success(notifications);
    }

    public async Task<OperationResult<ResendResult>> ResendAsync(
        int notificationId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        idempotencyKey = idempotencyKey.Trim();
        using var idempotencyLease = await _idempotency.AcquireAsync(
            $"{notificationId}:{idempotencyKey}", cancellationToken);
        var replay = await _db.OrderNotifications.SingleOrDefaultAsync(
            x => x.ResendOfNotificationId == notificationId && x.IdempotencyKey == idempotencyKey,
            cancellationToken);
        if (replay is not null)
        {
            return OperationResult<ResendResult>.Success(new ResendResult(replay.Id, true));
        }

        var source = await _db.OrderNotifications.SingleOrDefaultAsync(x => x.Id == notificationId, cancellationToken);
        if (source is null)
        {
            return OperationResult<ResendResult>.Fail(OperationError.NotFound, "Notification not found.");
        }

        if (source.ProviderMessageSid is not null)
        {
            if (!await RefreshAsync(source, cancellationToken))
            {
                return OperationResult<ResendResult>.Fail(
                    OperationError.ProviderUnavailable,
                    "The provider delivery outcome could not be confirmed.");
            }
        }
        if (!source.IsResendable)
        {
            return OperationResult<ResendResult>.Fail(OperationError.Conflict, "Only a message that failed to reach the shopper can be resent.");
        }

        var contact = await _db.ContactNumbers.SingleOrDefaultAsync(
            x => x.Id == source.ContactNumberId && x.DeletedAt == null,
            cancellationToken);
        if (contact is null)
        {
            return OperationResult<ResendResult>.Fail(OperationError.Conflict, "The destination is no longer registered.");
        }

        var order = await _db.Orders.AsNoTracking().SingleAsync(x => x.Id == source.OrderId, cancellationToken);
        if (order.Status == OrderStatus.Cancelled && source.Type == NotificationType.DeliveryFollowUp)
        {
            return OperationResult<ResendResult>.Fail(OperationError.Conflict, "A delivery follow-up cannot be resent for a cancelled order.");
        }

        var resend = new OrderNotification(
            source.OrderId,
            source.ContactNumberId,
            source.Type,
            source.Content!,
            UtcNow(),
            null,
            source.Id,
            idempotencyKey);
        _db.OrderNotifications.Add(resend);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _db.Entry(resend).State = EntityState.Detached;
            replay = await _db.OrderNotifications.AsNoTracking().SingleOrDefaultAsync(
                x => x.ResendOfNotificationId == notificationId && x.IdempotencyKey == idempotencyKey,
                cancellationToken);
            if (replay is not null)
            {
                return OperationResult<ResendResult>.Success(new ResendResult(replay.Id, true));
            }
            throw;
        }
        await SendAsync(resend, contact.CanonicalNumber, cancellationToken);
        return OperationResult<ResendResult>.Success(new ResendResult(resend.Id, false));
    }

    public async Task<OperationResult<bool>> DeleteContentAsync(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _db.OrderNotifications.SingleOrDefaultAsync(x => x.Id == notificationId, cancellationToken);
        if (notification is null)
        {
            return OperationResult<bool>.Fail(OperationError.NotFound, "Notification not found.");
        }
        if (notification.ContentDeletedAt is not null)
        {
            return OperationResult<bool>.Success(true);
        }

        if (notification.ProviderMessageSid is not null)
        {
            try
            {
                var provider = await _messaging.RedactAsync(notification.ProviderMessageSid, cancellationToken);
                ApplyProvider(notification, provider);
            }
            catch (Exception ex) when (IsProviderFailure(ex))
            {
                return OperationResult<bool>.Fail(
                    OperationError.ProviderUnavailable,
                    "The provider did not confirm content disposal.");
            }
        }

        notification.MarkContentDeleted(UtcNow());
        await _db.SaveChangesAsync(cancellationToken);
        return OperationResult<bool>.Success(true);
    }

    public async Task<ReconciliationResponse> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var providerMessages = await _messaging.ListAsync(from, to, cancellationToken);
        var local = await _db.OrderNotifications
            .Where(x => (x.CreatedAt >= from && x.CreatedAt <= to) ||
                        (x.ProviderDateSent != null && x.ProviderDateSent >= from && x.ProviderDateSent <= to))
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        var listedSids = providerMessages.Where(x => x.Sid != null)
            .Select(x => x.Sid!)
            .ToHashSet(StringComparer.Ordinal);
        var providerRecords = providerMessages.ToList();

        // The OpenAPI list operation filters on DateSent. Scheduled messages that were
        // canceled have no DateSent, so supplement only locally known SIDs and use the
        // provider-owned DateCreated value to avoid reporting a false application-only gap.
        foreach (var notification in local.Where(x => x.ProviderMessageSid != null && !listedSids.Contains(x.ProviderMessageSid)))
        {
            try
            {
                var provider = await _messaging.FetchAsync(notification.ProviderMessageSid!, cancellationToken);
                if (provider.DateCreated >= from && provider.DateCreated <= to)
                {
                    providerRecords.Add(provider);
                    listedSids.Add(notification.ProviderMessageSid!);
                }
            }
            catch (Exception ex) when (IsProviderFailure(ex))
            {
                // A provider record that cannot be fetched remains visible as application-only.
            }
        }

        var localBySid = local.Where(x => x.ProviderMessageSid != null)
            .ToDictionary(x => x.ProviderMessageSid!, StringComparer.Ordinal);
        var providerSids = providerRecords.Where(x => x.Sid != null)
            .Select(x => x.Sid!)
            .ToHashSet(StringComparer.Ordinal);
        var entries = new List<ReconciliationEntryDto>();

        foreach (var provider in providerRecords)
        {
            localBySid.TryGetValue(provider.Sid ?? string.Empty, out var notification);
            if (notification is not null)
            {
                ApplyProvider(notification, provider);
            }
            entries.Add(new ReconciliationEntryDto(
                notification is null ? "ProviderOnly" : "Matched",
                provider.Sid,
                notification?.Id,
                notification?.OrderId,
                provider.Status,
                notification?.ProviderStatus,
                provider.DateSent,
                notification?.CreatedAt));
        }

        foreach (var notification in local.Where(x => x.ProviderMessageSid is null || !providerSids.Contains(x.ProviderMessageSid)))
        {
            entries.Add(new ReconciliationEntryDto(
                "ApplicationOnly",
                notification.ProviderMessageSid,
                notification.Id,
                notification.OrderId,
                null,
                notification.ProviderStatus,
                null,
                notification.CreatedAt));
        }

        await _db.SaveChangesAsync(cancellationToken);
        return new ReconciliationResponse(
            from,
            to,
            entries.Count(x => x.Match == "Matched"),
            entries.Count(x => x.Match == "ProviderOnly"),
            entries.Count(x => x.Match == "ApplicationOnly"),
            entries);
    }

    public async Task RetryPendingCancellationsAsync(CancellationToken cancellationToken)
    {
        var pending = await _db.OrderNotifications
            .Where(x => x.ProviderStatus == "cancel_pending" && x.ProviderMessageSid != null)
            .Take(100)
            .ToListAsync(cancellationToken);
        foreach (var notification in pending)
        {
            await TryCancelAsync(notification, cancellationToken);
        }
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<List<OrderNotification>> CreateNotificationsAsync(
        int orderId,
        NotificationType type,
        string content,
        DateTimeOffset? scheduledFor,
        CancellationToken cancellationToken)
    {
        var contacts = await _db.ContactNumbers.AsNoTracking()
            .Where(x => x.DeletedAt == null && _db.Orders.Any(o => o.Id == orderId && o.BuyerId == x.BuyerId))
            .ToListAsync(cancellationToken);
        var notifications = contacts.Select(x =>
            new OrderNotification(orderId, x.Id, type, content, UtcNow(), scheduledFor)).ToList();
        _db.OrderNotifications.AddRange(notifications);
        await _db.SaveChangesAsync(cancellationToken);
        return notifications;
    }

    private async Task<IDbContextTransaction?> BeginPersistenceTransactionAsync(CancellationToken cancellationToken)
    {
        return _db.Database.IsRelational()
            ? await _db.Database.BeginTransactionAsync(cancellationToken)
            : null;
    }

    private async Task SendAllAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            var destination = await _db.ContactNumbers.AsNoTracking()
                .Where(x => x.Id == notification.ContactNumberId && x.DeletedAt == null)
                .Select(x => x.CanonicalNumber)
                .SingleOrDefaultAsync(cancellationToken);
            if (destination is not null)
            {
                await SendAsync(notification, destination, cancellationToken);
            }
        }
    }

    private async Task SendAsync(OrderNotification notification, string destination, CancellationToken cancellationToken)
    {
        try
        {
            var provider = await _messaging.SendAsync(destination, notification.Content!, notification.ScheduledFor, cancellationToken);
            if (string.IsNullOrWhiteSpace(provider.Sid))
            {
                notification.MarkProviderFailure(null, "The provider response did not include a message identifier.");
            }
            else
            {
                ApplyProvider(notification, provider);
            }
        }
        catch (Exception ex) when (IsProviderFailure(ex))
        {
            notification.MarkProviderFailure((ex as TwilioApiException)?.ProviderCode, "The provider did not accept the message.");
        }
        await _db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<bool> RefreshAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            var provider = await _messaging.FetchAsync(notification.ProviderMessageSid!, cancellationToken);
            ApplyProvider(notification, provider);
            return true;
        }
        catch (Exception ex) when (IsProviderFailure(ex))
        {
            // Keep the last provider state. Polling is best effort because callbacks are unavailable.
            return false;
        }
    }

    private async Task<bool> TryCancelAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        notification.RequestCancellation(UtcNow());
        try
        {
            var provider = await _messaging.CancelAsync(notification.ProviderMessageSid!, cancellationToken);
            ApplyProvider(notification, provider);
            if (provider.Status == "canceled")
            {
                return true;
            }

            notification.MarkCancellationFailure(provider.ErrorCode);
            return false;
        }
        catch (Exception ex) when (IsProviderFailure(ex))
        {
            notification.MarkCancellationFailure((ex as TwilioApiException)?.ProviderCode);
            return false;
        }
    }

    public static NotificationDto ToDto(OrderNotification x) => new(
        x.Id, x.OrderId, x.Type.ToString(), x.Content, x.CreatedAt, x.ScheduledFor,
        x.ProviderMessageSid, x.ProviderStatus, x.ProviderErrorCode, x.ProviderErrorMessage,
        x.ProviderDateCreated, x.ProviderDateSent, x.ContentDeletedAt, x.ResendOfNotificationId);

    private static NotificationSummaryDto ToSummary(OrderNotification x) => new(
        x.Id, x.Type.ToString(), x.ProviderStatus, x.ScheduledFor is not null, x.ProviderErrorCode);

    private static void ApplyProvider(OrderNotification notification, TwilioMessage provider)
    {
        if (string.IsNullOrWhiteSpace(provider.Sid))
        {
            return;
        }
        notification.ApplyProviderState(
            provider.Sid,
            provider.Status ?? "unknown",
            provider.ErrorCode,
            provider.ErrorMessage,
            provider.DateCreated,
            provider.DateSent);
    }

    private static bool IsProviderFailure(Exception exception) =>
        exception is TwilioApiException or HttpRequestException or TaskCanceledException or InvalidOperationException;

    private DateTimeOffset UtcNow() => _timeProvider.GetUtcNow();
}
