using System;
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

public sealed class OrderNotificationService : IOrderNotificationService
{
    private static readonly HashSet<string> ResendableStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "failed", "undelivered"
    };

    private readonly CatalogContext _context;
    private readonly ITwilioGateway _twilio;
    private readonly TimeProvider _timeProvider;

    public OrderNotificationService(CatalogContext context, ITwilioGateway twilio, TimeProvider timeProvider)
    {
        _context = context;
        _twilio = twilio;
        _timeProvider = timeProvider;
    }

    public async Task<ContactNumberResult> RegisterContactNumberAsync(string buyerId, string phoneNumber,
        CancellationToken cancellationToken)
    {
        var validation = await _twilio.ValidatePhoneNumberAsync(phoneNumber, cancellationToken);
        if (!validation.IsValid || string.IsNullOrWhiteSpace(validation.CanonicalNumber))
            throw new InvalidContactNumberException();

        var existing = await _context.ContactNumbers.SingleOrDefaultAsync(x =>
            x.BuyerId == buyerId && x.CanonicalNumber == validation.CanonicalNumber, cancellationToken);
        if (existing is not null) return Map(existing);

        var contact = new ContactNumber(buyerId, validation.CanonicalNumber, UtcNow());
        _context.ContactNumbers.Add(contact);
        await _context.SaveChangesAsync(cancellationToken);
        return Map(contact);
    }

    public async Task<IReadOnlyList<ContactNumberResult>> GetContactNumbersAsync(string buyerId,
        CancellationToken cancellationToken) => await _context.ContactNumbers.AsNoTracking()
        .Where(x => x.BuyerId == buyerId).OrderBy(x => x.Id).Select(x =>
            new ContactNumberResult(x.Id, x.CanonicalNumber, x.CreatedAt)).ToListAsync(cancellationToken);

    public async Task<bool> DeleteContactNumberAsync(string buyerId, int contactNumberId,
        CancellationToken cancellationToken)
    {
        var contact = await _context.ContactNumbers.SingleOrDefaultAsync(x =>
            x.Id == contactNumberId && x.BuyerId == buyerId, cancellationToken);
        if (contact is null) return false;
        _context.ContactNumbers.Remove(contact);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<PlaceOrderResult?> PlaceOrderAsync(string buyerId, ShippingAddressInput address,
        IReadOnlyList<OrderLineInput> items, CancellationToken cancellationToken)
    {
        if (items.Count == 0 || items.Any(x => x.CatalogItemId <= 0 || x.Quantity <= 0)) return null;
        var quantities = items.GroupBy(x => x.CatalogItemId)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.Quantity));
        var catalogItems = await _context.CatalogItems.AsNoTracking()
            .Where(x => quantities.Keys.Contains(x.Id)).ToListAsync(cancellationToken);
        if (catalogItems.Count != quantities.Count) return null;

        var orderItems = catalogItems.Select(item => new OrderItem(
            new CatalogItemOrdered(item.Id, item.Name, item.PictureUri), item.Price, quantities[item.Id])).ToList();
        var order = new Order(buyerId,
            new Address(address.Street, address.City, address.State, address.Country, address.ZipCode), orderItems);
        _context.Orders.Add(order);
        await _context.SaveChangesAsync(cancellationToken);

        var notifications = await NotifyAllContactsAsync(order, NotificationKind.OrderPlaced,
            $"eShopOnWeb: Your order {order.Id} has been placed.", null, cancellationToken);
        return new PlaceOrderResult(order.Id, notifications.Select(x => x.Id).ToList());
    }

    public async Task<OperationResult> DispatchOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _context.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        if (order is null) return new OperationResult(false, "Order not found.");
        if (!order.Dispatch(UtcNow())) return new OperationResult(false, "Only a placed order can be dispatched.");
        await _context.SaveChangesAsync(cancellationToken);

        await NotifyAllContactsAsync(order, NotificationKind.OrderDispatched,
            $"eShopOnWeb: Your order {order.Id} has been dispatched and is on its way.", null, cancellationToken);
        var followUpAt = UtcNow().AddDays(3);
        await NotifyAllContactsAsync(order, NotificationKind.DeliveryFollowUp,
            $"eShopOnWeb: How did delivery of order {order.Id} go?", followUpAt, cancellationToken);
        return new OperationResult(true);
    }

    public async Task<OperationResult> CancelOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _context.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        if (order is null) return new OperationResult(false, "Order not found.");
        if (order.Status == OrderStatus.Cancelled)
        {
            await CancelFollowUpsAsync(orderId, cancellationToken);
            return new OperationResult(true);
        }
        order.Cancel(UtcNow());
        await _context.SaveChangesAsync(cancellationToken);

        await CancelFollowUpsAsync(orderId, cancellationToken);
        await NotifyAllContactsAsync(order, NotificationKind.OrderCancelled,
            $"eShopOnWeb: Your order {order.Id} has been cancelled.", null, cancellationToken);
        return new OperationResult(true);
    }

    public async Task<IReadOnlyList<OrderResult>> GetOrdersAsync(string buyerId,
        CancellationToken cancellationToken)
    {
        var orders = await _context.Orders.AsNoTracking().Where(x => x.BuyerId == buyerId)
            .Include(x => x.OrderItems).OrderByDescending(x => x.OrderDate).ToListAsync(cancellationToken);
        var orderIds = orders.Select(x => x.Id).ToList();
        await SynchronizeAsync(_context.OrderNotifications.Where(x => orderIds.Contains(x.OrderId)).ToList(),
            cancellationToken);
        var notifications = await _context.OrderNotifications.AsNoTracking()
            .Where(x => orderIds.Contains(x.OrderId)).OrderBy(x => x.Id).ToListAsync(cancellationToken);
        return orders.Select(order => new OrderResult(order.Id, order.OrderDate, order.Status.ToString(), order.Total(),
            order.OrderItems.Select(x => new OrderLineResult(x.ItemOrdered.CatalogItemId, x.ItemOrdered.ProductName,
                x.UnitPrice, x.Units)).ToList(), notifications.Where(x => x.OrderId == order.Id).Select(Map).ToList()))
            .ToList();
    }

    public async Task<IReadOnlyList<NotificationResult>?> GetOrderNotificationsAsync(string buyerId, int orderId,
        CancellationToken cancellationToken)
    {
        if (!await _context.Orders.AnyAsync(x => x.Id == orderId && x.BuyerId == buyerId, cancellationToken))
            return null;
        var notifications = await _context.OrderNotifications.Where(x => x.OrderId == orderId)
            .OrderBy(x => x.Id).ToListAsync(cancellationToken);
        await SynchronizeAsync(notifications, cancellationToken);
        return notifications.Select(Map).ToList();
    }

    public async Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 200)
            return new ResendResult(false, null, "An idempotency key of 1 to 200 characters is required.");

        var prior = await _context.NotificationResendRequests.AsNoTracking()
            .SingleOrDefaultAsync(x => x.SourceNotificationId == notificationId &&
                x.IdempotencyKey == idempotencyKey, cancellationToken);
        if (prior is not null) return new ResendResult(true, prior.ResultNotificationId, null);

        var source = await _context.OrderNotifications.SingleOrDefaultAsync(x => x.Id == notificationId,
            cancellationToken);
        if (source is null) return new ResendResult(false, null, "Notification not found.");
        if (!string.IsNullOrWhiteSpace(source.ProviderMessageSid))
            await SynchronizeAsync(new[] { source }, cancellationToken);
        var order = await _context.Orders.AsNoTracking().SingleAsync(x => x.Id == source.OrderId, cancellationToken);
        if (source.Kind == NotificationKind.DeliveryFollowUp || order.Status == OrderStatus.Cancelled)
            return new ResendResult(false, null, "This notification must not be resent.");
        if (!ResendableStatuses.Contains(source.ProviderStatus))
            return new ResendResult(false, null, "Only a failed or undelivered notification can be resent.");
        if (string.IsNullOrWhiteSpace(source.Body))
            return new ResendResult(false, null, "Disposed notification content cannot be resent.");
        var contact = await _context.ContactNumbers.SingleOrDefaultAsync(x => x.Id == source.ContactNumberId,
            cancellationToken);
        if (contact is null)
            return new ResendResult(false, null, "The destination contact number is no longer registered.");

        var result = new OrderNotification(source.OrderId, source.BuyerId, source.ContactNumberId,
            NotificationKind.Resend, source.Body, UtcNow(), source.Id);
        var reservation = new NotificationResendRequest(source.Id, idempotencyKey, result, UtcNow());
        _context.NotificationResendRequests.Add(reservation);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _context.ChangeTracker.Clear();
            prior = await _context.NotificationResendRequests.AsNoTracking().SingleAsync(x =>
                x.SourceNotificationId == notificationId && x.IdempotencyKey == idempotencyKey, cancellationToken);
            return new ResendResult(true, prior.ResultNotificationId, null);
        }

        await SendAsync(result, contact.CanonicalNumber, null, cancellationToken);
        return new ResendResult(true, result.Id, null);
    }

    public async Task<OperationResult> DisposeContentAsync(int notificationId,
        CancellationToken cancellationToken)
    {
        var notification = await _context.OrderNotifications.SingleOrDefaultAsync(x => x.Id == notificationId,
            cancellationToken);
        if (notification is null) return new OperationResult(false, "Notification not found.");
        if (notification.ContentDisposedAt.HasValue) return new OperationResult(true);
        if (!string.IsNullOrWhiteSpace(notification.ProviderMessageSid))
        {
            try
            {
                var provider = await _twilio.RedactMessageAsync(notification.ProviderMessageSid, cancellationToken);
                notification.RefreshProviderStatus(provider.Status, provider.ErrorCode, provider.DateSent, UtcNow());
            }
            catch (TwilioGatewayException)
            {
                return new OperationResult(false, "The provider did not dispose of the message content.");
            }
        }
        notification.DisposeContent(UtcNow());
        await _context.SaveChangesAsync(cancellationToken);
        return new OperationResult(true);
    }

    public async Task<ReconciliationResult> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var provider = await _twilio.ListMessagesAsync(from, to, cancellationToken);
        var local = await _context.OrderNotifications.AsNoTracking().Where(x =>
            x.ProviderMessageSid != null && (x.ProviderDateSent ?? x.CreatedAt) >= from &&
            (x.ProviderDateSent ?? x.CreatedAt) <= to).ToListAsync(cancellationToken);
        var localBySid = local.ToDictionary(x => x.ProviderMessageSid!, StringComparer.Ordinal);
        var entries = new List<ReconciliationEntry>();
        foreach (var message in provider)
        {
            localBySid.Remove(message.Sid, out var match);
            entries.Add(new ReconciliationEntry(message.Sid, message.Status, message.DateSent,
                match?.Id, match is null ? "ProviderOnly" : "Matched"));
        }
        entries.AddRange(localBySid.Values.Select(x => new ReconciliationEntry(x.ProviderMessageSid!,
            x.ProviderStatus, x.ProviderDateSent, x.Id, "ApplicationOnly")));
        return new ReconciliationResult(from, to, entries.OrderBy(x => x.ProviderDateSent).ToList());
    }

    private async Task<List<OrderNotification>> NotifyAllContactsAsync(Order order, NotificationKind kind,
        string body, DateTimeOffset? sendAt, CancellationToken cancellationToken)
    {
        var contacts = await _context.ContactNumbers.Where(x => x.BuyerId == order.BuyerId)
            .OrderBy(x => x.Id).ToListAsync(cancellationToken);
        var notifications = new List<OrderNotification>();
        foreach (var contact in contacts)
        {
            var notification = new OrderNotification(order.Id, order.BuyerId, contact.Id, kind, body, UtcNow());
            _context.OrderNotifications.Add(notification);
            await _context.SaveChangesAsync(cancellationToken);
            await SendAsync(notification, contact.CanonicalNumber, sendAt, cancellationToken);
            notifications.Add(notification);
        }
        return notifications;
    }

    private async Task SendAsync(OrderNotification notification, string destination, DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var provider = await _twilio.SendMessageAsync(destination, notification.Body!, sendAt, cancellationToken);
            notification.RecordProviderResult(provider.Sid, provider.Status, provider.ErrorCode, provider.DateSent,
                UtcNow(), sendAt);
        }
        catch (TwilioGatewayException exception)
        {
            notification.RecordProviderFailure(exception.ProviderErrorCode, UtcNow());
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            notification.RecordProviderFailure(null, UtcNow());
        }
        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task CancelFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var followUps = await _context.OrderNotifications.Where(x => x.OrderId == orderId &&
            x.Kind == NotificationKind.DeliveryFollowUp && x.ProviderMessageSid != null).ToListAsync(cancellationToken);
        foreach (var notification in followUps)
        {
            ProviderMessage? provider = null;
            for (var attempt = 0; attempt < 5 && provider is null; attempt++)
            {
                try { provider = await _twilio.CancelMessageAsync(notification.ProviderMessageSid!, cancellationToken); }
                catch (TwilioGatewayException) when (attempt < 4)
                {
                    // A newly scheduled Message can briefly be unavailable on the instance endpoint.
                    await Task.Delay(TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt)), cancellationToken);
                }
                catch (TwilioGatewayException) { }
            }
            if (provider is not null)
                notification.RefreshProviderStatus(provider.Status, provider.ErrorCode, provider.DateSent, UtcNow());
        }
        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task SynchronizeAsync(IEnumerable<OrderNotification> notifications,
        CancellationToken cancellationToken)
    {
        var changed = false;
        foreach (var notification in notifications.Where(x => !string.IsNullOrWhiteSpace(x.ProviderMessageSid)))
        {
            try
            {
                var provider = await _twilio.FetchMessageAsync(notification.ProviderMessageSid!, cancellationToken);
                notification.RefreshProviderStatus(provider.Status, provider.ErrorCode, provider.DateSent, UtcNow());
                changed = true;
            }
            catch (TwilioGatewayException) { }
        }
        if (changed) await _context.SaveChangesAsync(cancellationToken);
    }

    private DateTimeOffset UtcNow() => _timeProvider.GetUtcNow();
    private static ContactNumberResult Map(ContactNumber x) => new(x.Id, x.CanonicalNumber, x.CreatedAt);
    private static NotificationResult Map(OrderNotification x) => new(x.Id, x.OrderId, x.Kind.ToString(), x.Body,
        x.ProviderMessageSid, x.ProviderStatus, x.ProviderErrorCode, x.CreatedAt, x.ProviderDateSent, x.ScheduledFor,
        x.LastProviderSyncAt, x.ContentDisposedAt, x.SourceNotificationId);
}

public sealed class InvalidContactNumberException : Exception { }
