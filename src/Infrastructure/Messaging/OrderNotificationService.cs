using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public sealed class OrderNotificationService(
    CatalogContext db,
    ITwilioMessagingGateway gateway,
    NotificationLockRegistry locks,
    TimeProvider timeProvider)
{
    private const int MaximumContactNumbers = 5;
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    public async Task<int> RegisterContactNumberAsync(string buyerId, string submittedNumber,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(submittedNumber))
            throw new NotificationOperationException(400, "phoneNumber is required.");

        PhoneValidationResult validation;
        try
        {
            validation = await gateway.ValidatePhoneNumberAsync(submittedNumber, cancellationToken);
        }
        catch (MessagingProviderException ex)
        {
            throw MapProviderFailure(ex, "Phone number validation is temporarily unavailable.");
        }

        if (!validation.IsValid || string.IsNullOrWhiteSpace(validation.CanonicalNumber))
            throw new NotificationOperationException(400, "The messaging provider does not consider this a usable destination.");

        using var ownerLock = await locks.AcquireAsync($"buyer:{buyerId}", cancellationToken);
        var active = await db.ContactNumbers.Where(x => x.BuyerId == buyerId && x.DeletedAt == null).ToListAsync(cancellationToken);
        var duplicate = active.FirstOrDefault(x => x.CanonicalNumber == validation.CanonicalNumber);
        if (duplicate is not null)
            throw new NotificationOperationException(409, "That contact number is already registered.");
        if (active.Count >= MaximumContactNumbers)
            throw new NotificationOperationException(409, $"A shopper may register at most {MaximumContactNumbers} contact numbers.");

        var entity = new ContactNumber(buyerId, validation.CanonicalNumber, timeProvider.GetUtcNow());
        db.ContactNumbers.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }

    public async Task<IReadOnlyList<ContactNumberView>> GetContactNumbersAsync(string buyerId,
        CancellationToken cancellationToken) =>
        await db.ContactNumbers.AsNoTracking()
            .Where(x => x.BuyerId == buyerId && x.DeletedAt == null)
            .OrderBy(x => x.Id)
            .Select(x => new ContactNumberView(x.Id, x.CanonicalNumber, x.CreatedAt))
            .ToListAsync(cancellationToken);

    public async Task DeleteContactNumberAsync(string buyerId, int contactNumberId,
        CancellationToken cancellationToken)
    {
        using var ownerLock = await locks.AcquireAsync($"buyer:{buyerId}", cancellationToken);
        var contact = await db.ContactNumbers.SingleOrDefaultAsync(
            x => x.Id == contactNumberId && x.BuyerId == buyerId && x.DeletedAt == null, cancellationToken);
        if (contact is null) throw new NotificationOperationException(404, "Contact number not found.");

        var scheduled = await db.OrderNotifications.Where(x =>
                x.ContactNumberId == contact.Id && x.Kind == NotificationKind.DeliveryFollowUp &&
                x.ProviderMessageSid != null && x.ProviderStatus != "canceled" && x.ProviderStatus != "delivered" &&
                x.ProviderStatus != "undelivered" && x.ProviderStatus != "failed" && x.ProviderStatus != "sent")
            .ToListAsync(cancellationToken);

        foreach (var notification in scheduled)
        {
            try
            {
                var snapshot = await gateway.CancelScheduledMessageAsync(notification.ProviderMessageSid!, cancellationToken);
                ApplySnapshot(notification, snapshot, timeProvider.GetUtcNow());
                if (!string.Equals(snapshot.Status, "canceled", StringComparison.OrdinalIgnoreCase))
                    throw new NotificationOperationException(502, "The provider did not confirm cancellation of a pending message.");
            }
            catch (MessagingProviderException ex)
            {
                throw MapProviderFailure(ex, "A pending message could not be cancelled; the contact number was not removed.");
            }
        }

        contact.Delete(timeProvider.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> requestedItems,
        ShippingAddressInput address, CancellationToken cancellationToken)
    {
        if (requestedItems.Count == 0) throw new NotificationOperationException(400, "At least one order item is required.");
        if (requestedItems.Any(x => x.CatalogItemId <= 0 || x.Quantity is <= 0 or > 100))
            throw new NotificationOperationException(400, "Each item requires a positive catalogItemId and quantity from 1 through 100.");
        ValidateAddress(address);

        var grouped = requestedItems.GroupBy(x => x.CatalogItemId)
            .Select(x => new { CatalogItemId = x.Key, Quantity = x.Sum(y => y.Quantity) }).ToList();
        if (grouped.Any(x => x.Quantity > 100)) throw new NotificationOperationException(400, "An item's total quantity cannot exceed 100.");

        var ids = grouped.Select(x => x.CatalogItemId).ToArray();
        var catalogItems = await db.CatalogItems.Where(x => ids.Contains(x.Id)).ToListAsync(cancellationToken);
        if (catalogItems.Count != ids.Length) throw new NotificationOperationException(400, "One or more catalog items do not exist.");

        var lines = grouped.Select(requested =>
        {
            var catalog = catalogItems.Single(x => x.Id == requested.CatalogItemId);
            return new OrderItem(new CatalogItemOrdered(catalog.Id, catalog.Name, catalog.PictureUri), catalog.Price, requested.Quantity);
        }).ToList();
        var order = new Order(buyerId,
            new Address(address.Street, address.City, address.State, address.Country, address.ZipCode), lines);
        db.Orders.Add(order);
        await db.SaveChangesAsync(cancellationToken);

        using (await locks.AcquireAsync($"buyer:{buyerId}", CancellationToken.None))
        {
            var contacts = await ActiveContactsAsync(buyerId, CancellationToken.None);
            foreach (var contact in contacts)
            {
                await CreateAndSendAsync(order.Id, contact, NotificationKind.OrderPlaced,
                    $"eShopOnWeb: Your order #{order.Id} was placed.", null, CancellationToken.None);
            }
        }
        return order.Id;
    }

    public async Task DispatchOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        if (order is null) throw new NotificationOperationException(404, "Order not found.");

        using var ownerLock = await locks.AcquireAsync($"buyer:{order.BuyerId}", cancellationToken);
        await db.Entry(order).ReloadAsync(cancellationToken);
        if (order.Status == OrderStatus.Cancelled) throw new NotificationOperationException(409, "A cancelled order cannot be dispatched.");
        if (order.Status == OrderStatus.Dispatched) return;

        var now = timeProvider.GetUtcNow();
        order.Dispatch(now);
        await db.SaveChangesAsync(cancellationToken);
        var contacts = await ActiveContactsAsync(order.BuyerId, CancellationToken.None);
        foreach (var contact in contacts)
        {
            await CreateAndSendAsync(order.Id, contact, NotificationKind.OrderDispatched,
                $"eShopOnWeb: Your order #{order.Id} is on its way.", null, CancellationToken.None);
            await CreateAndSendAsync(order.Id, contact, NotificationKind.DeliveryFollowUp,
                $"eShopOnWeb: How did delivery of order #{order.Id} go?", now.Add(FollowUpDelay), CancellationToken.None);
        }
    }

    public async Task CancelOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        if (order is null) throw new NotificationOperationException(404, "Order not found.");

        using var ownerLock = await locks.AcquireAsync($"buyer:{order.BuyerId}", cancellationToken);
        await db.Entry(order).ReloadAsync(cancellationToken);
        if (order.Status == OrderStatus.Cancelled) return;

        order.Cancel(timeProvider.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);

        var pending = await db.OrderNotifications.Where(x => x.OrderId == order.Id &&
            x.Kind == NotificationKind.DeliveryFollowUp && x.ProviderMessageSid != null &&
            x.ProviderStatus != "canceled" && x.ProviderStatus != "delivered" &&
            x.ProviderStatus != "undelivered" && x.ProviderStatus != "failed" && x.ProviderStatus != "sent")
            .ToListAsync(CancellationToken.None);
        foreach (var notification in pending)
        {
            await CancelBestEffortAsync(notification, CancellationToken.None);
        }

        var contacts = await ActiveContactsAsync(order.BuyerId, CancellationToken.None);
        foreach (var contact in contacts)
        {
            await CreateAndSendAsync(order.Id, contact, NotificationKind.OrderCancelled,
                $"eShopOnWeb: Your order #{order.Id} was cancelled.", null, CancellationToken.None);
        }
    }

    public async Task<IReadOnlyList<OrderView>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        var orders = await db.Orders.Include(x => x.OrderItems)
            .Where(x => x.BuyerId == buyerId).OrderByDescending(x => x.OrderDate).ToListAsync(cancellationToken);
        var ids = orders.Select(x => x.Id).ToArray();
        var notifications = await db.OrderNotifications.Where(x => ids.Contains(x.OrderId)).ToListAsync(cancellationToken);
        await RefreshAsync(notifications, cancellationToken);
        return orders.Select(order => new OrderView(order.Id, order.OrderDate, order.Status.ToString(), order.Total(),
            notifications.Where(x => x.OrderId == order.Id).OrderBy(x => x.Id).Select(ToView).ToList())).ToList();
    }

    public async Task<IReadOnlyList<NotificationView>> GetOrderNotificationsAsync(string buyerId, int orderId,
        CancellationToken cancellationToken)
    {
        if (!await db.Orders.AnyAsync(x => x.Id == orderId && x.BuyerId == buyerId, cancellationToken))
            throw new NotificationOperationException(404, "Order not found.");
        var notifications = await db.OrderNotifications.Where(x => x.OrderId == orderId).OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        await RefreshAsync(notifications, cancellationToken);
        return notifications.Select(ToView).ToList();
    }

    public async Task<int> ResendAsync(int sourceNotificationId, string idempotencyKey,
        CancellationToken cancellationToken)
    {
        idempotencyKey = idempotencyKey?.Trim() ?? string.Empty;
        if (idempotencyKey.Length is < 1 or > 128)
            throw new NotificationOperationException(400, "idempotencyKey must contain 1 through 128 characters.");

        using var requestLock = await locks.AcquireAsync($"resend:{sourceNotificationId}:{idempotencyKey}", cancellationToken);
        var existing = await db.NotificationResends.SingleOrDefaultAsync(x =>
            x.SourceNotificationId == sourceNotificationId && x.IdempotencyKey == idempotencyKey, cancellationToken);
        if (existing?.ResultNotificationId is int existingId) return existingId;
        if (existing is not null) throw new NotificationOperationException(409, "That idempotency request is already in progress.");

        var source = await db.OrderNotifications.SingleOrDefaultAsync(x => x.Id == sourceNotificationId, cancellationToken);
        if (source is null) throw new NotificationOperationException(404, "Notification not found.");
        if (!string.IsNullOrWhiteSpace(source.ProviderMessageSid))
            await RefreshAsync(new[] { source }, cancellationToken);
        if (!IsResendEligible(source))
            throw new NotificationOperationException(409, "Only failed or undelivered messages with retained content may be resent.");

        var contact = await db.ContactNumbers.SingleOrDefaultAsync(x =>
            x.Id == source.ContactNumberId && x.DeletedAt == null, cancellationToken);
        if (contact is null) throw new NotificationOperationException(409, "The destination is no longer registered.");

        var claim = new NotificationResend(source.Id, idempotencyKey, timeProvider.GetUtcNow());
        db.NotificationResends.Add(claim);
        await db.SaveChangesAsync(cancellationToken);

        var resend = new OrderNotification(source.OrderId, source.ContactNumberId, source.Kind, source.Body!,
            timeProvider.GetUtcNow(), source.Id);
        db.OrderNotifications.Add(resend);
        await db.SaveChangesAsync(cancellationToken);
        claim.SetResult(resend.Id);
        await db.SaveChangesAsync(cancellationToken);

        await SendExistingAsync(resend, contact.CanonicalNumber, null, CancellationToken.None);
        return resend.Id;
    }

    public async Task DisposeContentAsync(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await db.OrderNotifications.SingleOrDefaultAsync(x => x.Id == notificationId, cancellationToken);
        if (notification is null) throw new NotificationOperationException(404, "Notification not found.");
        if (notification.ContentDisposedAt is not null) return;
        if (string.IsNullOrWhiteSpace(notification.ProviderMessageSid))
        {
            notification.DisposeContent(timeProvider.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        try
        {
            await gateway.DisposeMessageContentAsync(notification.ProviderMessageSid, cancellationToken);
            var verified = await gateway.FetchMessageAsync(notification.ProviderMessageSid, cancellationToken);
            if (!string.IsNullOrEmpty(verified.Body))
                throw new NotificationOperationException(502, "The provider did not confirm content disposal.");
            ApplySnapshot(notification, verified, timeProvider.GetUtcNow());
            notification.DisposeContent(timeProvider.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (MessagingProviderException ex)
        {
            throw MapProviderFailure(ex, "Message content could not be disposed at the provider.");
        }
    }

    public async Task<ReconciliationView> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (from > to) throw new NotificationOperationException(400, "from must be earlier than or equal to to.");
        IReadOnlyList<ProviderMessageSnapshot> provider;
        try
        {
            provider = await gateway.ListMessagesAsync(from, to, cancellationToken);
        }
        catch (MessagingProviderException ex)
        {
            throw MapProviderFailure(ex, "Reconciliation could not be completed.");
        }

        var local = await db.OrderNotifications.AsNoTracking().Where(x =>
            (x.ProviderCreatedAt ?? x.CreatedAt) >= from && (x.ProviderCreatedAt ?? x.CreatedAt) <= to)
            .ToListAsync(cancellationToken);
        var localBySid = local.Where(x => x.ProviderMessageSid != null)
            .ToDictionary(x => x.ProviderMessageSid!, StringComparer.Ordinal);
        var providerSids = provider.Select(x => x.ProviderMessageSid).ToHashSet(StringComparer.Ordinal);
        var rows = new List<ReconciliationItem>();
        rows.AddRange(provider.Select(item =>
        {
            localBySid.TryGetValue(item.ProviderMessageSid, out var match);
            return new ReconciliationItem(match is null ? "ProviderOnly" : "Matched", item.ProviderMessageSid,
                match?.Id, match?.OrderId, item.Status, match?.ProviderStatus, item.DateSent ?? item.DateCreated);
        }));
        rows.AddRange(local.Where(x => x.ProviderMessageSid is null || !providerSids.Contains(x.ProviderMessageSid))
            .Select(x => new ReconciliationItem("LocalOnly", x.ProviderMessageSid, x.Id, x.OrderId, null,
                x.ProviderStatus, x.ProviderCreatedAt ?? x.CreatedAt)));
        return new ReconciliationView(from, to, rows.OrderBy(x => x.ProviderDate).ThenBy(x => x.NotificationId).ToList());
    }

    public async Task RetryPendingCancellationsAsync(CancellationToken cancellationToken)
    {
        var pending = await db.OrderNotifications.Where(x => x.CancellationPending && x.ProviderMessageSid != null)
            .ToListAsync(cancellationToken);
        foreach (var notification in pending) await CancelBestEffortAsync(notification, cancellationToken);
    }

    private async Task<List<ContactNumber>> ActiveContactsAsync(string buyerId, CancellationToken cancellationToken) =>
        await db.ContactNumbers.Where(x => x.BuyerId == buyerId && x.DeletedAt == null).OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);

    private async Task<OrderNotification> CreateAndSendAsync(int orderId, ContactNumber contact,
        NotificationKind kind, string body, DateTimeOffset? scheduledFor, CancellationToken cancellationToken)
    {
        var notification = new OrderNotification(orderId, contact.Id, kind, body, timeProvider.GetUtcNow());
        db.OrderNotifications.Add(notification);
        await db.SaveChangesAsync(cancellationToken);
        await SendExistingAsync(notification, contact.CanonicalNumber, scheduledFor, cancellationToken);
        return notification;
    }

    private async Task SendExistingAsync(OrderNotification notification, string destination,
        DateTimeOffset? scheduledFor, CancellationToken cancellationToken)
    {
        var result = await gateway.SendMessageAsync(destination, notification.Body!, scheduledFor, cancellationToken);
        if (result.Accepted && result.Message is not null)
            ApplySnapshot(notification, result.Message, timeProvider.GetUtcNow(), scheduledFor);
        else
            notification.RecordProviderFailure(result.FailureStatus, timeProvider.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task CancelBestEffortAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        notification.RequestCancellation();
        try
        {
            var snapshot = await gateway.CancelScheduledMessageAsync(notification.ProviderMessageSid!, cancellationToken);
            ApplySnapshot(notification, snapshot, timeProvider.GetUtcNow());
            if (!string.Equals(snapshot.Status, "canceled", StringComparison.OrdinalIgnoreCase))
                notification.RequestCancellation();
        }
        catch (MessagingProviderException)
        {
            notification.MarkRefreshFailed(timeProvider.GetUtcNow());
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task RefreshAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications.Where(x => !string.IsNullOrWhiteSpace(x.ProviderMessageSid)))
        {
            try
            {
                var snapshot = await gateway.FetchMessageAsync(notification.ProviderMessageSid!, cancellationToken);
                ApplySnapshot(notification, snapshot, timeProvider.GetUtcNow());
            }
            catch (MessagingProviderException)
            {
                notification.MarkRefreshFailed(timeProvider.GetUtcNow());
            }
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private static void ApplySnapshot(OrderNotification notification, ProviderMessageSnapshot snapshot,
        DateTimeOffset observedAt, DateTimeOffset? scheduledFor = null) =>
        notification.RecordProviderResult(snapshot.ProviderMessageSid, snapshot.Status, snapshot.ErrorCode,
            snapshot.DateCreated, snapshot.DateSent, snapshot.DateUpdated, observedAt, scheduledFor);

    private static NotificationView ToView(OrderNotification notification) => new(
        notification.Id, notification.OrderId, notification.Kind.ToString(), notification.Body, notification.ProviderStatus,
        notification.ProviderMessageSid, notification.ProviderErrorCode, notification.CreatedAt,
        notification.ScheduledFor, notification.ProviderSentAt, notification.LastRefreshedAt,
        notification.LastRefreshFailedAt is not null, notification.ContentDisposedAt is not null);

    private static bool IsResendEligible(OrderNotification notification) =>
        notification.Body is not null && notification.Kind != NotificationKind.DeliveryFollowUp &&
        (string.Equals(notification.ProviderStatus, "failed", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(notification.ProviderStatus, "undelivered", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(notification.ProviderStatus, "Failed", StringComparison.Ordinal));

    private static void ValidateAddress(ShippingAddressInput address)
    {
        if (address is null || string.IsNullOrWhiteSpace(address.Street) || string.IsNullOrWhiteSpace(address.City) ||
            string.IsNullOrWhiteSpace(address.Country) || string.IsNullOrWhiteSpace(address.ZipCode))
            throw new NotificationOperationException(400, "A complete shippingAddress is required.");
    }

    private static NotificationOperationException MapProviderFailure(MessagingProviderException ex, string safeMessage)
    {
        var status = ex.StatusCode switch
        {
            429 => 503,
            401 or 403 => 502,
            >= 400 and < 500 => ex.StatusCode.Value,
            _ => 502
        };
        return new NotificationOperationException(status, safeMessage);
    }
}
