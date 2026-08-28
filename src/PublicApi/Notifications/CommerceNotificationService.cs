using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

public sealed class CommerceNotificationService
{
    private readonly CatalogContext _db;
    private readonly IMessageProvider _provider;
    private readonly IUriComposer _uriComposer;
    private readonly ResendCoordinator _resendCoordinator;

    public CommerceNotificationService(CatalogContext db, IMessageProvider provider,
        IUriComposer uriComposer, ResendCoordinator resendCoordinator)
    {
        _db = db;
        _provider = provider;
        _uriComposer = uriComposer;
        _resendCoordinator = resendCoordinator;
    }

    public async Task<ContactNumber> RegisterContactAsync(string buyerId, string submittedNumber,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(submittedNumber))
            throw new ApiProblemException(400, "A mobile number is required.");

        if (await _db.ContactNumbers.CountAsync(x => x.BuyerId == buyerId && x.DeletedAt == null,
                cancellationToken) >= 3)
            throw new ApiProblemException(409, "At most three active mobile numbers may be registered.");

        string canonical;
        try
        {
            canonical = await _provider.ValidateAndCanonicalizeAsync(submittedNumber, cancellationToken);
        }
        catch (MessageProviderException ex)
        {
            throw MapProviderRegistrationError(ex);
        }

        var existing = await _db.ContactNumbers.FirstOrDefaultAsync(
            x => x.BuyerId == buyerId && x.DeletedAt == null && x.Number == canonical, cancellationToken);
        if (existing != null) return existing;

        var contact = new ContactNumber(buyerId, canonical, DateTimeOffset.UtcNow);
        _db.ContactNumbers.Add(contact);
        await _db.SaveChangesAsync(cancellationToken);
        return contact;
    }

    public Task<List<ContactNumber>> GetContactsAsync(string buyerId, CancellationToken cancellationToken) =>
        _db.ContactNumbers.AsNoTracking()
            .Where(x => x.BuyerId == buyerId && x.DeletedAt == null)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);

    public async Task DeleteContactAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken)
    {
        var contact = await _db.ContactNumbers.FirstOrDefaultAsync(
            x => x.Id == contactNumberId && x.BuyerId == buyerId && x.DeletedAt == null, cancellationToken);
        if (contact == null) throw new ApiProblemException(404, "Contact number not found.");

        var now = DateTimeOffset.UtcNow;
        contact.Delete(now);
        var scheduled = await _db.OrderNotifications.Where(x =>
            x.ContactNumberId == contactNumberId && x.Kind == NotificationKind.DeliveryFollowUp &&
            x.ProviderMessageSid != null && x.ProviderStatus != "canceled").ToListAsync(cancellationToken);
        foreach (var notification in scheduled) notification.RequestCancellation(now);
        await _db.SaveChangesAsync(cancellationToken);

        foreach (var notification in scheduled)
            await TryCancelScheduledAsync(notification, cancellationToken);
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> requestedItems,
        Address shippingAddress, CancellationToken cancellationToken)
    {
        if (requestedItems.Count == 0) throw new ApiProblemException(400, "At least one catalog item is required.");
        if (requestedItems.Any(x => x.Quantity <= 0)) throw new ApiProblemException(400, "Every quantity must be positive.");

        var consolidated = requestedItems.GroupBy(x => x.CatalogItemId)
            .Select(x => new OrderLineInput(x.Key, x.Sum(y => y.Quantity))).ToList();
        var ids = consolidated.Select(x => x.CatalogItemId).ToList();
        var catalogItems = await _db.CatalogItems.Where(x => ids.Contains(x.Id)).ToListAsync(cancellationToken);
        if (catalogItems.Count != ids.Distinct().Count())
            throw new ApiProblemException(400, "One or more catalog items do not exist.");

        var orderItems = consolidated.Select(line =>
        {
            var item = catalogItems.Single(x => x.Id == line.CatalogItemId);
            return new OrderItem(new CatalogItemOrdered(item.Id, item.Name,
                _uriComposer.ComposePicUri(item.PictureUri)), item.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shippingAddress, orderItems);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);

        await NotifyActiveContactsAsync(order, NotificationKind.OrderPlaced,
            $"Your eShop order #{order.Id} has been placed.", false, cancellationToken);
        return order;
    }

    public async Task<Order> DispatchAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders.FirstOrDefaultAsync(x => x.Id == orderId, cancellationToken)
            ?? throw new ApiProblemException(404, "Order not found.");
        if (!order.Dispatch(DateTimeOffset.UtcNow))
            throw new ApiProblemException(409, "Only a placed order can be dispatched.");

        await _db.SaveChangesAsync(cancellationToken);
        await NotifyActiveContactsAsync(order, NotificationKind.OrderDispatched,
            $"Your eShop order #{order.Id} is on its way.", false, cancellationToken);
        await NotifyActiveContactsAsync(order, NotificationKind.DeliveryFollowUp,
            $"How did delivery of eShop order #{order.Id} go?", true, cancellationToken);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders.FirstOrDefaultAsync(x => x.Id == orderId, cancellationToken)
            ?? throw new ApiProblemException(404, "Order not found.");
        if (order.FulfillmentStatus == OrderFulfillmentStatus.Cancelled) return order;

        order.Cancel(DateTimeOffset.UtcNow);
        var now = DateTimeOffset.UtcNow;
        var followUps = await _db.OrderNotifications.Where(x => x.OrderId == orderId &&
            x.Kind == NotificationKind.DeliveryFollowUp && x.ProviderMessageSid != null &&
            x.ProviderStatus != "canceled").ToListAsync(cancellationToken);
        foreach (var notification in followUps) notification.RequestCancellation(now);
        await _db.SaveChangesAsync(cancellationToken);

        foreach (var notification in followUps)
            await TryCancelScheduledAsync(notification, cancellationToken);

        await NotifyActiveContactsAsync(order, NotificationKind.OrderCancelled,
            $"Your eShop order #{order.Id} has been cancelled.", false, cancellationToken);
        return order;
    }

    public async Task<List<OrderSummary>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        var orders = await _db.Orders.AsNoTracking().Include(x => x.OrderItems)
            .Where(x => x.BuyerId == buyerId).OrderByDescending(x => x.OrderDate).ToListAsync(cancellationToken);
        var orderIds = orders.Select(x => x.Id).ToList();
        var notifications = await _db.OrderNotifications.Where(x => orderIds.Contains(x.OrderId))
            .ToListAsync(cancellationToken);
        await RefreshNotificationsAsync(notifications, cancellationToken);

        return orders.Select(order => new OrderSummary(order.Id, order.OrderDate,
            order.FulfillmentStatus.ToString(), order.Total(), notifications.Where(x => x.OrderId == order.Id)
                .OrderBy(x => x.CreatedAt).Select(ToNotificationSummary).ToList())).ToList();
    }

    public async Task<List<NotificationSummary>> GetOrderNotificationsAsync(string buyerId, int orderId,
        CancellationToken cancellationToken)
    {
        if (!await _db.Orders.AnyAsync(x => x.Id == orderId && x.BuyerId == buyerId, cancellationToken))
            throw new ApiProblemException(404, "Order not found.");

        var notifications = await _db.OrderNotifications.Where(x => x.OrderId == orderId)
            .OrderBy(x => x.CreatedAt).ToListAsync(cancellationToken);
        await RefreshNotificationsAsync(notifications, cancellationToken);
        return notifications.Select(ToNotificationSummary).ToList();
    }

    public async Task<int> ResendAsync(int sourceNotificationId, string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 200)
            throw new ApiProblemException(400, "An idempotency key of at most 200 characters is required.");

        await using var lease = await _resendCoordinator.AcquireAsync(sourceNotificationId, idempotencyKey,
            cancellationToken);
        var existingClaim = await _db.NotificationResendClaims.FirstOrDefaultAsync(x =>
            x.SourceNotificationId == sourceNotificationId && x.IdempotencyKey == idempotencyKey, cancellationToken);
        if (existingClaim?.ProducedNotificationId is int existingId) return existingId;

        var source = await _db.OrderNotifications.FirstOrDefaultAsync(x => x.Id == sourceNotificationId,
            cancellationToken) ?? throw new ApiProblemException(404, "Notification not found.");
        if (source.Body == null) throw new ApiProblemException(409, "Disposed notification content cannot be resent.");

        if (source.ProviderMessageSid != null)
        {
            await RefreshNotificationsAsync(new[] { source }, cancellationToken);
        }

        if (!IsResendable(source.ProviderStatus))
            throw new ApiProblemException(409, "Only a notification that failed or was undelivered can be resent.");

        var order = await _db.Orders.AsNoTracking().FirstOrDefaultAsync(x => x.Id == source.OrderId, cancellationToken)
            ?? throw new ApiProblemException(404, "Order not found.");
        if (source.Kind == NotificationKind.DeliveryFollowUp && order.FulfillmentStatus == OrderFulfillmentStatus.Cancelled)
            throw new ApiProblemException(409, "A cancelled order's delivery follow-up cannot be resent.");

        var contact = await _db.ContactNumbers.FirstOrDefaultAsync(x => x.Id == source.ContactNumberId &&
            x.BuyerId == source.BuyerId && x.DeletedAt == null, cancellationToken)
            ?? throw new ApiProblemException(409, "The destination is no longer registered.");

        await using var transaction = _db.Database.IsRelational()
            ? await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        existingClaim = await _db.NotificationResendClaims.FirstOrDefaultAsync(x =>
            x.SourceNotificationId == sourceNotificationId && x.IdempotencyKey == idempotencyKey, cancellationToken);
        if (existingClaim?.ProducedNotificationId is int completedId) return completedId;

        var claim = existingClaim ?? new NotificationResendClaim(sourceNotificationId, idempotencyKey,
            DateTimeOffset.UtcNow);
        if (existingClaim == null)
        {
            _db.NotificationResendClaims.Add(claim);
            await _db.SaveChangesAsync(cancellationToken);
        }

        var resend = new OrderNotification(source.OrderId, source.BuyerId, contact.Id,
            NotificationKind.Resend, source.Body, DateTimeOffset.UtcNow, source.Id);
        _db.OrderNotifications.Add(resend);
        await _db.SaveChangesAsync(cancellationToken);
        claim.SetProducedNotification(resend.Id);
        await _db.SaveChangesAsync(cancellationToken);
        if (transaction != null) await transaction.CommitAsync(cancellationToken);

        await TrySendAsync(resend, contact.Number, false, cancellationToken);
        return resend.Id;
    }

    public async Task DisposeContentAsync(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _db.OrderNotifications.FirstOrDefaultAsync(x => x.Id == notificationId,
            cancellationToken) ?? throw new ApiProblemException(404, "Notification not found.");
        if (notification.ContentDisposedAt != null) return;

        if (notification.ProviderMessageSid != null)
        {
            try
            {
                var snapshot = await _provider.DisposeContentAsync(notification.ProviderMessageSid, cancellationToken);
                notification.ApplyProviderSnapshot(snapshot, DateTimeOffset.UtcNow);
            }
            catch (MessageProviderException ex)
            {
                throw MapProviderOperatorError(ex);
            }
        }

        notification.DisposeContent(DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (from > to) throw new ApiProblemException(400, "The from instant must not be after to.");
        IReadOnlyList<ProviderMessageRecord> provider;
        try
        {
            provider = await _provider.ListSentAsync(from, to, cancellationToken);
        }
        catch (MessageProviderException ex)
        {
            throw MapProviderOperatorError(ex);
        }

        var local = await _db.OrderNotifications.AsNoTracking().Where(x => x.ProviderMessageSid != null &&
            x.ProviderDateSent >= from && x.ProviderDateSent <= to).ToListAsync(cancellationToken);
        var localBySid = local.ToDictionary(x => x.ProviderMessageSid!, StringComparer.Ordinal);
        var providerBySid = provider.ToDictionary(x => x.Sid, StringComparer.Ordinal);
        var rows = new List<ReconciliationRow>();

        foreach (var message in provider)
        {
            localBySid.TryGetValue(message.Sid, out var notification);
            rows.Add(new ReconciliationRow(message.Sid, notification?.Id,
                notification == null ? "provider-only" : "matched", message.Status,
                notification?.ProviderStatus, message.DateSent));
        }
        foreach (var notification in local.Where(x => !providerBySid.ContainsKey(x.ProviderMessageSid!)))
        {
            rows.Add(new ReconciliationRow(notification.ProviderMessageSid!, notification.Id,
                "local-only", null, notification.ProviderStatus, notification.ProviderDateSent));
        }

        return new ReconciliationReport(from, to, rows.OrderBy(x => x.DateSent).ToList());
    }

    public async Task RetryPendingCancellationsAsync(CancellationToken cancellationToken)
    {
        var pending = await _db.OrderNotifications.Where(x => x.CancellationRequestedAt != null &&
            x.ProviderMessageSid != null && x.ProviderStatus != "canceled" && x.ProviderStatus != "delivered" &&
            x.ProviderStatus != "sent" && x.ProviderStatus != "failed" && x.ProviderStatus != "undelivered")
            .Take(100).ToListAsync(cancellationToken);
        foreach (var notification in pending)
            await TryCancelScheduledAsync(notification, cancellationToken);
    }

    private async Task NotifyActiveContactsAsync(Order order, NotificationKind kind, string body,
        bool scheduled, CancellationToken cancellationToken)
    {
        var contacts = await _db.ContactNumbers.Where(x => x.BuyerId == order.BuyerId && x.DeletedAt == null)
            .OrderBy(x => x.Id).ToListAsync(cancellationToken);
        foreach (var contact in contacts)
        {
            var notification = new OrderNotification(order.Id, order.BuyerId, contact.Id, kind, body,
                DateTimeOffset.UtcNow);
            if (scheduled) notification.SetScheduledFor(DateTimeOffset.UtcNow.AddDays(3));
            _db.OrderNotifications.Add(notification);
            await _db.SaveChangesAsync(cancellationToken);
            await TrySendAsync(notification, contact.Number, scheduled, cancellationToken);
        }
    }

    private async Task TrySendAsync(OrderNotification notification, string canonicalNumber, bool scheduled,
        CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = scheduled
                ? await _provider.ScheduleAsync(canonicalNumber, notification.Body!, notification.ScheduledFor!.Value,
                    cancellationToken)
                : await _provider.SendAsync(canonicalNumber, notification.Body!, cancellationToken);
            notification.ApplyProviderSnapshot(snapshot, DateTimeOffset.UtcNow);
        }
        catch (MessageProviderException ex)
        {
            notification.MarkProviderFailure(DateTimeOffset.UtcNow, ex.ProviderStatusCode);
        }
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task TryCancelScheduledAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        if (notification.ProviderMessageSid == null) return;
        try
        {
            var snapshot = await _provider.CancelAsync(notification.ProviderMessageSid, cancellationToken);
            notification.ApplyProviderSnapshot(snapshot, DateTimeOffset.UtcNow);
        }
        catch (MessageProviderException)
        {
            notification.MarkSyncFailure(DateTimeOffset.UtcNow);
        }
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task RefreshNotificationsAsync(IEnumerable<OrderNotification> notifications,
        CancellationToken cancellationToken)
    {
        foreach (var notification in notifications.Where(x => x.ProviderMessageSid != null))
        {
            try
            {
                var snapshot = await _provider.FetchAsync(notification.ProviderMessageSid!, cancellationToken);
                notification.ApplyProviderSnapshot(snapshot, DateTimeOffset.UtcNow);
            }
            catch (MessageProviderException)
            {
                notification.MarkSyncFailure(DateTimeOffset.UtcNow);
            }
        }
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static bool IsResendable(string status) => status is "failed" or "undelivered" or "provider_error";

    private static NotificationSummary ToNotificationSummary(OrderNotification notification) =>
        new(notification.Id, notification.Kind.ToString(), notification.ProviderMessageSid,
            notification.ProviderStatus, notification.ProviderErrorCode, notification.Body,
            notification.CreatedAt, notification.ProviderDateSent, notification.ScheduledFor,
            notification.ContentDisposedAt, notification.LastSyncFailedAt != null);

    private static ApiProblemException MapProviderRegistrationError(MessageProviderException ex) =>
        ex.ProviderStatusCode switch
        {
            422 or 400 or 404 => new ApiProblemException(422, "The mobile number is not a usable destination."),
            429 => new ApiProblemException(503, "Mobile number validation is temporarily unavailable."),
            _ => new ApiProblemException(502, "Mobile number validation is unavailable.")
        };

    private static ApiProblemException MapProviderOperatorError(MessageProviderException ex) =>
        ex.ProviderStatusCode == 429
            ? new ApiProblemException(503, "The messaging provider is temporarily unavailable.")
            : new ApiProblemException(502, "The messaging provider operation could not be completed.");
}

public sealed class ResendCoordinator
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public async Task<IAsyncDisposable> AcquireAsync(int notificationId, string key, CancellationToken cancellationToken)
    {
        var lockKey = $"{notificationId}:{key}";
        var semaphore = _locks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        return new Lease(semaphore);
    }

    private sealed class Lease : IAsyncDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        public Lease(SemaphoreSlim semaphore) => _semaphore = semaphore;
        public ValueTask DisposeAsync()
        {
            _semaphore.Release();
            return ValueTask.CompletedTask;
        }
    }
}

public sealed class ApiProblemException : Exception
{
    public ApiProblemException(int statusCode, string message) : base(message) => StatusCode = statusCode;
    public int StatusCode { get; }
}

public sealed record OrderLineInput(int CatalogItemId, int Quantity);
public sealed record NotificationSummary(int NotificationId, string Kind, string? ProviderMessageSid,
    string ProviderStatus, int? ProviderErrorCode, string? Content, DateTimeOffset CreatedAt,
    DateTimeOffset? SentAt, DateTimeOffset? ScheduledFor, DateTimeOffset? ContentDisposedAt, bool SyncFailed);
public sealed record OrderSummary(int OrderId, DateTimeOffset OrderDate, string Status, decimal Total,
    IReadOnlyList<NotificationSummary> Notifications);
public sealed record ReconciliationRow(string ProviderMessageSid, int? NotificationId, string Match,
    string? ProviderStatus, string? LocalStatus, DateTimeOffset? DateSent);
public sealed record ReconciliationReport(DateTimeOffset From, DateTimeOffset To,
    IReadOnlyList<ReconciliationRow> Messages);
