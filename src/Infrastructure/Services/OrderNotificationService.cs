using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Twilio;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public sealed class OrderNotificationService : IOrderNotificationService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ResendLocks = new(StringComparer.Ordinal);
    private static readonly HashSet<string> TerminalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "delivered", "failed", "undelivered", "canceled", "read", "partially_delivered"
    };

    private readonly CatalogContext _db;
    private readonly ITwilioMessagingGateway _twilio;

    public OrderNotificationService(CatalogContext db, ITwilioMessagingGateway twilio)
    {
        _db = db;
        _twilio = twilio;
    }

    public async Task<ContactNumberView> RegisterContactNumberAsync(string buyerId, string phoneNumber, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw new ContactNumberValidationException("A phone number is required.");
        }

        string canonical;
        try
        {
            canonical = await _twilio.ValidateAndCanonicalizeAsync(phoneNumber, ct);
        }
        catch (NotificationProviderException ex) when (ex.StatusCode is >= 400 and < 500)
        {
            throw new ContactNumberValidationException("The phone number is not a usable SMS destination.");
        }

        var existing = await _db.ContactNumbers
            .SingleOrDefaultAsync(x => x.BuyerId == buyerId && x.CanonicalNumber == canonical, ct);
        if (existing is not null)
        {
            return Map(existing);
        }

        var contactNumber = new ContactNumber(buyerId, canonical);
        _db.ContactNumbers.Add(contactNumber);
        await _db.SaveChangesAsync(ct);
        return Map(contactNumber);
    }

    public async Task<IReadOnlyList<ContactNumberView>> GetContactNumbersAsync(string buyerId, CancellationToken ct) =>
        await _db.ContactNumbers.AsNoTracking()
            .Where(x => x.BuyerId == buyerId)
            .OrderBy(x => x.Id)
            .Select(x => new ContactNumberView(x.Id, x.CanonicalNumber, x.RegisteredAt))
            .ToListAsync(ct);

    public async Task<bool> DeleteContactNumberAsync(string buyerId, int contactNumberId, CancellationToken ct)
    {
        var contact = await _db.ContactNumbers.SingleOrDefaultAsync(
            x => x.Id == contactNumberId && x.BuyerId == buyerId, ct);
        if (contact is null)
        {
            return false;
        }

        var scheduled = await _db.OrderNotifications
            .Where(x => x.ContactNumberId == contactNumberId &&
                        x.Kind == NotificationKind.DeliveryFollowUp &&
                        x.ProviderMessageSid != null &&
                        !TerminalStatuses.Contains(x.ProviderStatus))
            .ToListAsync(ct);

        foreach (var notification in scheduled)
        {
            notification.RequestCancellation();
            try
            {
                notification.ApplyProviderState(await _twilio.CancelAsync(notification.ProviderMessageSid!, ct));
            }
            catch (NotificationProviderException)
            {
                await _db.SaveChangesAsync(CancellationToken.None);
                throw new NotificationProviderException(
                    "The number was retained because Twilio could not confirm cancellation of a pending message.");
            }
        }

        _db.ContactNumbers.Remove(contact);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<int> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<OrderLineInput> items,
        Address shippingAddress,
        CancellationToken ct)
    {
        if (items.Count == 0 || items.Any(x => x.CatalogItemId <= 0 || x.Quantity <= 0))
        {
            throw new OrderRequestValidationException("At least one catalog item with a positive quantity is required.");
        }

        var requested = items
            .GroupBy(x => x.CatalogItemId)
            .Select(x => new OrderLineInput(x.Key, x.Sum(i => i.Quantity)))
            .ToList();
        var ids = requested.Select(x => x.CatalogItemId).ToArray();
        var catalogItems = await _db.CatalogItems.Where(x => ids.Contains(x.Id)).ToListAsync(ct);
        if (catalogItems.Count != ids.Length)
        {
            throw new OrderRequestValidationException("One or more catalog items do not exist.");
        }

        var orderItems = requested.Select(line =>
        {
            var catalogItem = catalogItems.Single(x => x.Id == line.CatalogItemId);
            return new OrderItem(
                new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri),
                catalogItem.Price,
                line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shippingAddress, orderItems);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(ct);

        var contacts = await ActiveContactsAsync(buyerId, ct);
        foreach (var contact in contacts)
        {
            await CreateAndSendAsync(
                order,
                contact,
                NotificationKind.OrderPlaced,
                $"eShopOnWeb: Order {order.Id} has been placed.",
                null,
                ct);
        }

        return order.Id;
    }

    public async Task<bool> DispatchOrderAsync(int orderId, CancellationToken ct)
    {
        var order = await _db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, ct);
        if (order is null)
        {
            return false;
        }

        if (!order.MarkDispatched())
        {
            return true;
        }

        await _db.SaveChangesAsync(ct);
        var contacts = await ActiveContactsAsync(order.BuyerId, ct);
        var followUpAt = DateTimeOffset.UtcNow.AddDays(3);

        foreach (var contact in contacts)
        {
            await CreateAndSendAsync(
                order,
                contact,
                NotificationKind.OrderDispatched,
                $"eShopOnWeb: Order {order.Id} has been dispatched and is on its way.",
                null,
                ct);
            await CreateAndSendAsync(
                order,
                contact,
                NotificationKind.DeliveryFollowUp,
                $"eShopOnWeb: How did delivery of order {order.Id} go?",
                followUpAt,
                ct);
        }

        return true;
    }

    public async Task<bool> CancelOrderAsync(int orderId, CancellationToken ct)
    {
        var order = await _db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, ct);
        if (order is null)
        {
            return false;
        }

        if (!order.Cancel())
        {
            return true;
        }

        await _db.SaveChangesAsync(ct);
        var followUps = await _db.OrderNotifications
            .Where(x => x.OrderId == order.Id &&
                        x.Kind == NotificationKind.DeliveryFollowUp &&
                        x.ProviderMessageSid != null &&
                        !TerminalStatuses.Contains(x.ProviderStatus))
            .ToListAsync(ct);

        foreach (var followUp in followUps)
        {
            followUp.RequestCancellation();
            try
            {
                followUp.ApplyProviderState(await _twilio.CancelAsync(followUp.ProviderMessageSid!, ct));
            }
            catch (Exception ex) when (ex is NotificationProviderException or OperationCanceledException)
            {
                followUp.RecordProviderFailure("Twilio cancellation is pending retry.");
                followUp.RequestCancellation();
            }
        }

        await _db.SaveChangesAsync(CancellationToken.None);

        var contacts = await ActiveContactsAsync(order.BuyerId, CancellationToken.None);
        foreach (var contact in contacts)
        {
            await CreateAndSendAsync(
                order,
                contact,
                NotificationKind.OrderCancelled,
                $"eShopOnWeb: Order {order.Id} has been cancelled.",
                null,
                CancellationToken.None);
        }

        return true;
    }

    public async Task<IReadOnlyList<OrderView>> GetMyOrdersAsync(string buyerId, CancellationToken ct)
    {
        var orders = await _db.Orders.AsNoTracking()
            .Include(x => x.OrderItems)
            .Where(x => x.BuyerId == buyerId)
            .OrderByDescending(x => x.OrderDate)
            .ToListAsync(ct);
        var notifications = await _db.OrderNotifications
            .Where(x => x.BuyerId == buyerId)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(ct);

        await RefreshAsync(notifications, ct);
        return orders.Select(order => new OrderView(
            order.Id,
            order.OrderDate,
            order.Status.ToString(),
            order.Total(),
            notifications.Where(x => x.OrderId == order.Id).Select(Map).ToList())).ToList();
    }

    public async Task<IReadOnlyList<NotificationView>?> GetOrderNotificationsAsync(string buyerId, int orderId, CancellationToken ct)
    {
        if (!await _db.Orders.AsNoTracking().AnyAsync(x => x.Id == orderId && x.BuyerId == buyerId, ct))
        {
            return null;
        }

        var notifications = await _db.OrderNotifications
            .Where(x => x.OrderId == orderId && x.BuyerId == buyerId)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(ct);
        await RefreshAsync(notifications, ct);
        return notifications.Select(Map).ToList();
    }

    public async Task<int?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 128)
        {
            throw new NotificationActionException("An idempotency key of 1 to 128 characters is required.");
        }

        var lockKey = $"{notificationId}:{idempotencyKey}";
        var gate = ResendLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var existing = await _db.NotificationResendRequests.AsNoTracking()
                .SingleOrDefaultAsync(x => x.SourceNotificationId == notificationId && x.IdempotencyKey == idempotencyKey, ct);
            if (existing is not null)
            {
                return existing.NotificationId;
            }

            var source = await _db.OrderNotifications.SingleOrDefaultAsync(x => x.Id == notificationId, ct);
            if (source is null)
            {
                return null;
            }

            if (source.ProviderMessageSid is not null && !TerminalStatuses.Contains(source.ProviderStatus))
            {
                try
                {
                    source.ApplyProviderState(await _twilio.FetchAsync(source.ProviderMessageSid, ct));
                    await _db.SaveChangesAsync(ct);
                }
                catch (NotificationProviderException)
                {
                    throw new NotificationActionException("The current delivery outcome could not be confirmed.");
                }
            }

            if (!CanResend(source.ProviderStatus))
            {
                throw new NotificationActionException("Only a message that did not reach the shopper can be resent.");
            }

            if (source.ContentDisposed || string.IsNullOrWhiteSpace(source.Body))
            {
                throw new NotificationActionException("Disposed message content cannot be resent.");
            }

            var contact = await _db.ContactNumbers.SingleOrDefaultAsync(
                x => x.Id == source.ContactNumberId && x.BuyerId == source.BuyerId, ct);
            if (contact is null)
            {
                throw new NotificationActionException("The destination is no longer registered.");
            }

            var resend = new OrderNotification(
                source.OrderId,
                source.BuyerId,
                source.ContactNumberId,
                NotificationKind.Resend,
                source.Body,
                originalNotificationId: source.Id);
            _db.OrderNotifications.Add(resend);
            await _db.SaveChangesAsync(ct);

            _db.NotificationResendRequests.Add(new NotificationResendRequest(source.Id, idempotencyKey, resend.Id));
            await _db.SaveChangesAsync(ct);
            await SendExistingAsync(resend, contact.CanonicalNumber, null, ct);
            return resend.Id;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<bool> DisposeContentAsync(int notificationId, CancellationToken ct)
    {
        var notification = await _db.OrderNotifications.SingleOrDefaultAsync(x => x.Id == notificationId, ct);
        if (notification is null)
        {
            return false;
        }

        if (notification.ContentDisposed)
        {
            return true;
        }

        if (notification.ProviderMessageSid is not null)
        {
            var originalBody = notification.Body;
            var providerState = await _twilio.RedactAsync(notification.ProviderMessageSid, ct);
            if (!string.IsNullOrEmpty(originalBody) && string.Equals(providerState.Body, originalBody, StringComparison.Ordinal))
            {
                throw new NotificationProviderException("Twilio still exposes the original message content.");
            }

            notification.ApplyProviderState(providerState);
        }

        notification.MarkContentDisposed();
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<ReconciliationView>> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        if (from > to)
        {
            throw new NotificationActionException("The from date must not be later than the to date.");
        }

        var provider = await _twilio.ListAsync(from, to, ct);
        var local = await _db.OrderNotifications.AsNoTracking()
            .Where(x => x.CreatedAt >= from && x.CreatedAt <= to)
            .ToListAsync(ct);
        var localBySid = local
            .Where(x => x.ProviderMessageSid != null)
            .ToDictionary(x => x.ProviderMessageSid!, StringComparer.Ordinal);
        var report = new List<ReconciliationView>();

        foreach (var message in provider)
        {
            localBySid.Remove(message.Sid ?? string.Empty, out var match);
            report.Add(new ReconciliationView(
                match is null ? "provider_only" : "matched",
                message.Sid,
                match?.Id,
                message.Status,
                match?.ProviderStatus,
                message.DateCreated,
                match?.CreatedAt));
        }

        var providerSids = provider.Where(x => x.Sid is not null).Select(x => x.Sid!).ToHashSet(StringComparer.Ordinal);
        foreach (var notification in local.Where(x => x.ProviderMessageSid is null || !providerSids.Contains(x.ProviderMessageSid)))
        {
            report.Add(new ReconciliationView(
                "application_only",
                notification.ProviderMessageSid,
                notification.Id,
                null,
                notification.ProviderStatus,
                null,
                notification.CreatedAt));
        }

        return report;
    }

    public async Task RetryPendingCancellationsAsync(CancellationToken ct)
    {
        var pending = await _db.OrderNotifications
            .Where(x => x.CancellationPending && x.ProviderMessageSid != null)
            .ToListAsync(ct);
        foreach (var notification in pending)
        {
            try
            {
                notification.ApplyProviderState(await _twilio.CancelAsync(notification.ProviderMessageSid!, ct));
            }
            catch (Exception ex) when (ex is NotificationProviderException or OperationCanceledException)
            {
                notification.RequestCancellation();
            }
        }

        await _db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task CreateAndSendAsync(
        Order order,
        ContactNumber contact,
        NotificationKind kind,
        string body,
        DateTimeOffset? scheduledFor,
        CancellationToken ct)
    {
        var notification = new OrderNotification(order.Id, order.BuyerId, contact.Id, kind, body, scheduledFor);
        _db.OrderNotifications.Add(notification);
        await _db.SaveChangesAsync(CancellationToken.None);
        await SendExistingAsync(notification, contact.CanonicalNumber, scheduledFor, ct);
    }

    private async Task SendExistingAsync(
        OrderNotification notification,
        string canonicalNumber,
        DateTimeOffset? scheduledFor,
        CancellationToken ct)
    {
        try
        {
            var state = scheduledFor.HasValue
                ? await _twilio.ScheduleAsync(canonicalNumber, notification.Body!, scheduledFor.Value, ct)
                : await _twilio.SendAsync(canonicalNumber, notification.Body!, ct);
            notification.ApplyProviderState(state);
        }
        catch (Exception ex) when (ex is NotificationProviderException or OperationCanceledException)
        {
            notification.RecordProviderFailure("The notification could not be submitted to Twilio.");
        }

        await _db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task RefreshAsync(List<OrderNotification> notifications, CancellationToken ct)
    {
        var changed = false;
        foreach (var notification in notifications.Where(x =>
                     x.ProviderMessageSid is not null && !TerminalStatuses.Contains(x.ProviderStatus)))
        {
            try
            {
                notification.ApplyProviderState(await _twilio.FetchAsync(notification.ProviderMessageSid!, ct));
                changed = true;
            }
            catch (Exception ex) when (ex is NotificationProviderException or OperationCanceledException)
            {
                // Preserve the last known provider outcome; reporting remains available while Twilio is unavailable.
            }
        }

        if (changed)
        {
            await _db.SaveChangesAsync(CancellationToken.None);
        }
    }

    private Task<List<ContactNumber>> ActiveContactsAsync(string buyerId, CancellationToken ct) =>
        _db.ContactNumbers.Where(x => x.BuyerId == buyerId).OrderBy(x => x.Id).ToListAsync(ct);

    private static bool CanResend(string status) =>
        status.Equals("failed", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("undelivered", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("partially_delivered", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("provider_error", StringComparison.OrdinalIgnoreCase);

    private static ContactNumberView Map(ContactNumber number) =>
        new(number.Id, number.CanonicalNumber, number.RegisteredAt);

    private static NotificationView Map(OrderNotification notification) => new(
        notification.Id,
        notification.OrderId,
        notification.Kind.ToString(),
        notification.ContentDisposed ? null : notification.Body,
        notification.ProviderStatus,
        notification.ProviderMessageSid,
        notification.ProviderErrorCode,
        notification.ProviderErrorMessage,
        notification.CreatedAt,
        notification.ScheduledFor,
        notification.ContentDisposed,
        notification.CancellationPending,
        notification.LastProviderSyncAt,
        notification.OriginalNotificationId);
}
