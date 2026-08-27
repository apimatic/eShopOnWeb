using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.Infrastructure.Notifications;

public sealed class OrderNotificationCoordinator
{
    public static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ResendLocks = new();
    private readonly CatalogContext _db;
    private readonly ITwilioClient _twilio;

    public OrderNotificationCoordinator(CatalogContext db, ITwilioClient twilio)
    {
        _db = db;
        _twilio = twilio;
    }

    public async Task<ContactNumber?> RegisterContactNumberAsync(
        string buyerId,
        string suppliedNumber,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(suppliedNumber)) return null;

        var lookup = await _twilio.LookupPhoneNumberAsync(suppliedNumber, cancellationToken);
        if (!lookup.Valid || string.IsNullOrWhiteSpace(lookup.PhoneNumber)) return null;

        var existing = await _db.ContactNumbers.SingleOrDefaultAsync(
            x => x.BuyerId == buyerId && x.PhoneNumber == lookup.PhoneNumber && x.DeletedAt == null,
            cancellationToken);
        if (existing is not null) return existing;

        var contact = new ContactNumber(buyerId, lookup.PhoneNumber, DateTimeOffset.UtcNow);
        _db.ContactNumbers.Add(contact);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _db.Entry(contact).State = EntityState.Detached;
            existing = await _db.ContactNumbers.SingleOrDefaultAsync(
                x => x.BuyerId == buyerId && x.PhoneNumber == lookup.PhoneNumber && x.DeletedAt == null,
                cancellationToken);
            if (existing is not null) return existing;
            throw;
        }
        return contact;
    }

    public Task<List<ContactNumber>> ListContactNumbersAsync(string buyerId, CancellationToken cancellationToken) =>
        _db.ContactNumbers
            .Where(x => x.BuyerId == buyerId && x.DeletedAt == null)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<bool> DeleteContactNumberAsync(
        string buyerId,
        int contactNumberId,
        CancellationToken cancellationToken)
    {
        var contact = await _db.ContactNumbers.SingleOrDefaultAsync(
            x => x.Id == contactNumberId && x.BuyerId == buyerId && x.DeletedAt == null,
            cancellationToken);
        if (contact is null) return false;

        contact.Delete(DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);

        var scheduled = await _db.OrderNotifications
            .Where(x => x.ContactNumberId == contactNumberId &&
                        x.Kind == NotificationKind.DeliveryFollowUp &&
                        x.ProviderMessageSid != null &&
                        x.ProviderStatus != "canceled" &&
                        x.ProviderSentAt == null)
            .ToListAsync(cancellationToken);

        foreach (var notification in scheduled)
            await TryCancelScheduledAsync(notification, cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyCollection<OrderLineInput> lines,
        AddressInput? shippingAddress,
        CancellationToken cancellationToken)
    {
        if (lines.Count == 0 || lines.Any(x => x.CatalogItemId <= 0 || x.Quantity <= 0))
            throw new ArgumentException("Every order line must have a valid catalog item id and a positive quantity.");

        var normalized = lines
            .GroupBy(x => x.CatalogItemId)
            .Select(x => new OrderLineInput(x.Key, x.Sum(y => y.Quantity)))
            .ToList();

        var ids = normalized.Select(x => x.CatalogItemId).ToArray();

        var catalogItems = await _db.CatalogItems.Where(x => ids.Contains(x.Id)).ToListAsync(cancellationToken);
        if (catalogItems.Count != ids.Length)
            throw new ArgumentException("One or more catalog items do not exist.");

        var itemMap = catalogItems.ToDictionary(x => x.Id);
        var orderItems = normalized.Select(line =>
        {
            CatalogItem item = itemMap[line.CatalogItemId];
            return new OrderItem(
                new CatalogItemOrdered(item.Id, item.Name, item.PictureUri),
                item.Price,
                line.Quantity);
        }).ToList();

        var address = shippingAddress is null
            ? new Address("Not provided", "Not provided", string.Empty, "Not provided", "Not provided")
            : new Address(
                Required(shippingAddress.Street, "street"),
                Required(shippingAddress.City, "city"),
                shippingAddress.State ?? string.Empty,
                Required(shippingAddress.Country, "country"),
                Required(shippingAddress.ZipCode, "zipCode"));

        var order = new Order(buyerId, address, orderItems);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);

        await NotifyAllAsync(
            order,
            NotificationKind.OrderPlaced,
            $"Your eShopOnWeb order #{order.Id} has been placed.",
            null,
            cancellationToken);
        return order;
    }

    public async Task<OrderActionResult> DispatchOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        if (order is null) return OrderActionResult.NotFound;
        if (!order.Dispatch(DateTimeOffset.UtcNow)) return OrderActionResult.Conflict;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return OrderActionResult.Conflict;
        }

        await NotifyAllAsync(
            order,
            NotificationKind.OrderDispatched,
            $"Your eShopOnWeb order #{order.Id} is on its way.",
            null,
            cancellationToken);
        await NotifyAllAsync(
            order,
            NotificationKind.DeliveryFollowUp,
            $"How did delivery of your eShopOnWeb order #{order.Id} go?",
            DateTimeOffset.UtcNow.Add(FollowUpDelay),
            cancellationToken);
        return OrderActionResult.Success;
    }

    public async Task<OrderActionResult> CancelOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        if (order is null) return OrderActionResult.NotFound;
        if (!order.Cancel(DateTimeOffset.UtcNow)) return OrderActionResult.Conflict;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return OrderActionResult.Conflict;
        }

        var scheduled = await _db.OrderNotifications
            .Where(x => x.OrderId == orderId &&
                        x.Kind == NotificationKind.DeliveryFollowUp &&
                        x.ProviderMessageSid != null &&
                        x.ProviderStatus != "canceled" &&
                        x.ProviderSentAt == null)
            .ToListAsync(cancellationToken);
        foreach (var notification in scheduled)
            await TryCancelScheduledAsync(notification, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        await NotifyAllAsync(
            order,
            NotificationKind.OrderCancelled,
            $"Your eShopOnWeb order #{order.Id} has been cancelled.",
            null,
            cancellationToken);
        return OrderActionResult.Success;
    }

    public async Task<List<Order>> GetOrdersAsync(string buyerId, CancellationToken cancellationToken) =>
        await _db.Orders
            .AsNoTracking()
            .Where(x => x.BuyerId == buyerId)
            .Include(x => x.OrderItems)
            .OrderByDescending(x => x.OrderDate)
            .ToListAsync(cancellationToken);

    public async Task<List<OrderNotification>?> GetOrderNotificationsAsync(
        string buyerId,
        int orderId,
        CancellationToken cancellationToken)
    {
        if (!await _db.Orders.AnyAsync(x => x.Id == orderId && x.BuyerId == buyerId, cancellationToken))
            return null;

        var notifications = await _db.OrderNotifications
            .Where(x => x.OrderId == orderId)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
        await RefreshAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<IReadOnlyDictionary<int, List<OrderNotification>>> GetNotificationSummariesAsync(
        IReadOnlyCollection<int> orderIds,
        CancellationToken cancellationToken)
    {
        var notifications = await _db.OrderNotifications
            .Where(x => orderIds.Contains(x.OrderId))
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
        await RefreshAsync(notifications, cancellationToken);
        return notifications.GroupBy(x => x.OrderId).ToDictionary(x => x.Key, x => x.ToList());
    }

    public async Task<ResendResult> ResendAsync(
        int notificationId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 200)
            return new ResendResult(ResendOutcome.Invalid, null);

        var lockKey = $"{notificationId}:{idempotencyKey}";
        var gate = ResendLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var previous = await _db.NotificationResends.SingleOrDefaultAsync(
                x => x.NotificationId == notificationId && x.IdempotencyKey == idempotencyKey,
                cancellationToken);
            if (previous is not null)
                return previous.ResultNotificationId is int resultId
                    ? new ResendResult(ResendOutcome.Success, resultId)
                    : new ResendResult(ResendOutcome.InProgress, null);

            var original = await _db.OrderNotifications.SingleOrDefaultAsync(x => x.Id == notificationId, cancellationToken);
            if (original is null) return new ResendResult(ResendOutcome.NotFound, null);
            if (original.Content is null) return new ResendResult(ResendOutcome.ContentDisposed, null);

            if (original.ProviderMessageSid is not null)
                await RefreshAsync(new[] { original }, cancellationToken);
            if (original.ProviderStatus is not ("failed" or "undelivered"))
                return new ResendResult(ResendOutcome.NotEligible, null);

            var order = await _db.Orders.SingleAsync(x => x.Id == original.OrderId, cancellationToken);
            if (order.Status == OrderStatus.Cancelled && original.Kind == NotificationKind.DeliveryFollowUp)
                return new ResendResult(ResendOutcome.NotEligible, null);

            var contact = original.ContactNumberId is int contactId
                ? await _db.ContactNumbers.SingleOrDefaultAsync(
                    x => x.Id == contactId && x.DeletedAt == null,
                    cancellationToken)
                : null;
            if (contact is null) return new ResendResult(ResendOutcome.ContactRemoved, null);

            var operation = new NotificationResend(notificationId, idempotencyKey, DateTimeOffset.UtcNow);
            _db.NotificationResends.Add(operation);
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                _db.Entry(operation).State = EntityState.Detached;
                previous = await _db.NotificationResends.SingleOrDefaultAsync(
                    x => x.NotificationId == notificationId && x.IdempotencyKey == idempotencyKey,
                    cancellationToken);
                if (previous is not null)
                    return previous.ResultNotificationId is int resultId
                        ? new ResendResult(ResendOutcome.Success, resultId)
                        : new ResendResult(ResendOutcome.InProgress, null);
                throw;
            }

            var resend = new OrderNotification(
                original.OrderId,
                contact.Id,
                NotificationKind.Resend,
                original.Content,
                DateTimeOffset.UtcNow,
                sourceNotificationId: original.Id);
            _db.OrderNotifications.Add(resend);
            await _db.SaveChangesAsync(cancellationToken);
            await SendAsync(resend, contact.PhoneNumber, null, cancellationToken);
            operation.Complete(resend.Id);
            await _db.SaveChangesAsync(cancellationToken);
            return new ResendResult(ResendOutcome.Success, resend.Id);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<DisposeContentOutcome> DisposeContentAsync(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _db.OrderNotifications.SingleOrDefaultAsync(x => x.Id == notificationId, cancellationToken);
        if (notification is null) return DisposeContentOutcome.NotFound;
        if (notification.ContentDisposedAt is not null) return DisposeContentOutcome.Success;

        if (notification.ProviderMessageSid is not null)
        {
            try
            {
                await _twilio.RedactMessageAsync(notification.ProviderMessageSid, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                return DisposeContentOutcome.ProviderFailure;
            }
        }

        notification.DisposeContent(DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);
        return DisposeContentOutcome.Success;
    }

    public async Task<List<ReconciliationEntry>> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var provider = await _twilio.ListMessagesAsync(from, to, cancellationToken);
        var providerBySid = provider.Where(x => !string.IsNullOrWhiteSpace(x.Sid)).ToDictionary(x => x.Sid);
        var local = await _db.OrderNotifications
            .Where(x => x.CreatedAt >= from && x.CreatedAt <= to)
            .ToListAsync(cancellationToken);

        foreach (var sidBatch in providerBySid.Keys.Chunk(500))
        {
            var batch = sidBatch.ToArray();
            var matches = await _db.OrderNotifications
                .Where(x => x.ProviderMessageSid != null && batch.Contains(x.ProviderMessageSid))
                .ToListAsync(cancellationToken);
            foreach (var match in matches)
                if (local.All(x => x.Id != match.Id)) local.Add(match);
        }

        var localBySid = local
            .Where(x => x.ProviderMessageSid is not null)
            .ToDictionary(x => x.ProviderMessageSid!);
        var allSids = providerBySid.Keys.Union(localBySid.Keys).OrderBy(x => x);
        var entries = new List<ReconciliationEntry>();
        foreach (var sid in allSids)
        {
            providerBySid.TryGetValue(sid, out var providerMessage);
            localBySid.TryGetValue(sid, out var localMessage);
            entries.Add(new ReconciliationEntry(
                sid,
                providerMessage is null ? "eshopOnly" : localMessage is null ? "providerOnly" : "matched",
                providerMessage,
                localMessage));
        }

        foreach (var failedLocal in local.Where(x => x.ProviderMessageSid is null))
            entries.Add(new ReconciliationEntry(null, "eshopOnly", null, failedLocal));

        return entries;
    }

    public async Task RetryPendingCancellationsAsync(CancellationToken cancellationToken)
    {
        var pending = await _db.OrderNotifications
            .Where(x => x.CancellationPending && x.ProviderMessageSid != null)
            .OrderBy(x => x.CreatedAt)
            .Take(100)
            .ToListAsync(cancellationToken);
        foreach (var notification in pending)
            await TryCancelScheduledAsync(notification, cancellationToken);
        if (pending.Count > 0) await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task NotifyAllAsync(
        Order order,
        NotificationKind kind,
        string content,
        DateTimeOffset? scheduledFor,
        CancellationToken cancellationToken)
    {
        var contacts = await _db.ContactNumbers
            .Where(x => x.BuyerId == order.BuyerId && x.DeletedAt == null)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
        foreach (var contact in contacts)
        {
            var notification = new OrderNotification(
                order.Id,
                contact.Id,
                kind,
                content,
                DateTimeOffset.UtcNow,
                scheduledFor);
            _db.OrderNotifications.Add(notification);
            await _db.SaveChangesAsync(cancellationToken);
            await SendAsync(notification, contact.PhoneNumber, scheduledFor, cancellationToken);
        }
    }

    private async Task SendAsync(
        OrderNotification notification,
        string destination,
        DateTimeOffset? scheduledFor,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _twilio.SendMessageAsync(destination, notification.Content!, scheduledFor, cancellationToken);
            notification.RecordProviderState(
                result.Sid,
                result.Status,
                result.ErrorCode,
                result.ErrorMessage,
                result.DateCreated,
                result.DateSent,
                DateTimeOffset.UtcNow);
        }
        catch (TwilioProviderException ex)
        {
            notification.RecordSendFailure(ex.StatusCode, ex.Message, DateTimeOffset.UtcNow);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            notification.RecordSendFailure(null, "The Twilio response could not be processed.", DateTimeOffset.UtcNow);
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task RefreshAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        var changed = false;
        foreach (var notification in notifications.Where(x => x.ProviderMessageSid is not null))
        {
            try
            {
                var state = await _twilio.FetchMessageAsync(notification.ProviderMessageSid!, cancellationToken);
                notification.RefreshProviderState(
                    state.Status,
                    state.ErrorCode,
                    state.ErrorMessage,
                    state.DateCreated,
                    state.DateSent,
                    DateTimeOffset.UtcNow);
                changed = true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // A read failure leaves the last provider state intact and does not fail the API read.
            }
        }

        if (changed) await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task TryCancelScheduledAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            var state = await _twilio.CancelMessageAsync(notification.ProviderMessageSid!, cancellationToken);
            notification.RefreshProviderState(
                state.Status,
                state.ErrorCode,
                state.ErrorMessage,
                state.DateCreated,
                state.DateSent,
                DateTimeOffset.UtcNow);
            if (state.Status != "canceled") notification.MarkCancellationPending();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            notification.MarkCancellationPending();
        }
    }

    private static string Required(string? value, string field) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"Shipping address {field} is required.") : value;
}

public sealed record OrderLineInput(int CatalogItemId, int Quantity);
public sealed record AddressInput(string? Street, string? City, string? State, string? Country, string? ZipCode);
public enum OrderActionResult { Success, NotFound, Conflict }
public enum ResendOutcome { Success, NotFound, Invalid, InProgress, NotEligible, ContentDisposed, ContactRemoved }
public sealed record ResendResult(ResendOutcome Outcome, int? NotificationId);
public enum DisposeContentOutcome { Success, NotFound, ProviderFailure }
public sealed record ReconciliationEntry(
    string? ProviderMessageSid,
    string Match,
    TwilioMessageRecord? Provider,
    OrderNotification? Eshop);
