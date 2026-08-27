using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public sealed class OrderNotificationService : IOrderNotificationService
{
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);
    private readonly CatalogContext _db;
    private readonly ITwilioGateway _twilio;
    private readonly ResendCoordinator _resendCoordinator;
    private readonly TimeProvider _clock;

    public OrderNotificationService(CatalogContext db, ITwilioGateway twilio,
        ResendCoordinator resendCoordinator, TimeProvider clock)
    {
        _db = db;
        _twilio = twilio;
        _resendCoordinator = resendCoordinator;
        _clock = clock;
    }

    public async Task<ServiceResult<ContactNumberView>> RegisterContactNumberAsync(string buyerId,
        string phoneNumber, string? countryCode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
            return ServiceResult<ContactNumberView>.Fail(ServiceFailure.Invalid, "phoneNumber is required.");

        PhoneNumberLookup lookup;
        try
        {
            lookup = await _twilio.LookupPhoneNumberAsync(phoneNumber, countryCode, cancellationToken);
        }
        catch (TwilioProviderException)
        {
            return ServiceResult<ContactNumberView>.Fail(ServiceFailure.ProviderUnavailable,
                "The phone number could not be validated by the provider.");
        }
        catch (HttpRequestException)
        {
            return ServiceResult<ContactNumberView>.Fail(ServiceFailure.ProviderUnavailable,
                "The phone number could not be validated by the provider.");
        }

        if (!lookup.Valid || string.IsNullOrWhiteSpace(lookup.CanonicalNumber))
            return ServiceResult<ContactNumberView>.Fail(ServiceFailure.Invalid,
                "The phone number is not a usable destination.");

        var existing = await _db.ContactNumbers.FirstOrDefaultAsync(x => x.BuyerId == buyerId &&
            x.CanonicalNumber == lookup.CanonicalNumber && x.RemovedAt == null, cancellationToken);
        if (existing is not null)
            return ServiceResult<ContactNumberView>.Success(ToView(existing));

        var contact = new ContactNumber(buyerId, lookup.CanonicalNumber, Now);
        _db.ContactNumbers.Add(contact);
        await _db.SaveChangesAsync(cancellationToken);
        return ServiceResult<ContactNumberView>.Success(ToView(contact));
    }

    public async Task<IReadOnlyList<ContactNumberView>> GetContactNumbersAsync(string buyerId,
        CancellationToken cancellationToken) =>
        (await _db.ContactNumbers.AsNoTracking()
            .Where(x => x.BuyerId == buyerId && x.RemovedAt == null)
            .OrderBy(x => x.Id).ToListAsync(cancellationToken)).Select(ToView).ToList();

    public async Task<ServiceResult<bool>> RemoveContactNumberAsync(string buyerId, int contactNumberId,
        CancellationToken cancellationToken)
    {
        var contact = await _db.ContactNumbers.FirstOrDefaultAsync(x => x.Id == contactNumberId &&
            x.BuyerId == buyerId && x.RemovedAt == null, cancellationToken);
        if (contact is null)
            return ServiceResult<bool>.Fail(ServiceFailure.NotFound, "Contact number was not found.");
        contact.Remove(Now);
        await _db.SaveChangesAsync(cancellationToken);
        await CancelScheduledNotificationsAsync(
            await _db.OrderNotifications.Where(x => x.ContactNumberId == contactNumberId &&
                x.ScheduledFor != null && x.ProviderMessageSid != null && x.ProviderStatus != "canceled" &&
                x.ProviderStatus != "delivered" && x.ProviderStatus != "failed" &&
                x.ProviderStatus != "undelivered").ToListAsync(cancellationToken), cancellationToken);
        return ServiceResult<bool>.Success(true);
    }

    public async Task<ServiceResult<OrderView>> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineInput>? items,
        ShippingAddressInput? shippingAddress, CancellationToken cancellationToken)
    {
        if (items is null || items.Count == 0 || items.Any(x => x.CatalogItemId <= 0 || x.Quantity <= 0))
            return ServiceResult<OrderView>.Fail(ServiceFailure.Invalid,
                "At least one catalog item with a positive quantity is required.");
        if (shippingAddress is not null && (string.IsNullOrWhiteSpace(shippingAddress.Street) ||
            string.IsNullOrWhiteSpace(shippingAddress.City) || string.IsNullOrWhiteSpace(shippingAddress.Country) ||
            string.IsNullOrWhiteSpace(shippingAddress.ZipCode)))
            return ServiceResult<OrderView>.Fail(ServiceFailure.Invalid,
                "A supplied shipping address requires street, city, country and zipCode.");

        var grouped = items.GroupBy(x => x.CatalogItemId)
            .Select(x => new OrderLineInput(x.Key, x.Sum(y => y.Quantity))).ToList();
        var ids = grouped.Select(x => x.CatalogItemId).ToList();
        var catalogItems = await _db.CatalogItems.Where(x => ids.Contains(x.Id)).ToListAsync(cancellationToken);
        if (catalogItems.Count != ids.Count)
            return ServiceResult<OrderView>.Fail(ServiceFailure.Invalid, "One or more catalog items do not exist.");

        var lines = grouped.Select(line =>
        {
            var item = catalogItems.Single(x => x.Id == line.CatalogItemId);
            return new OrderItem(new CatalogItemOrdered(item.Id, item.Name, item.PictureUri), item.Price, line.Quantity);
        }).ToList();
        var address = shippingAddress is null
            ? new Address("Not supplied", "Not supplied", string.Empty, "Not supplied", "Not supplied")
            : new Address(shippingAddress.Street, shippingAddress.City, shippingAddress.State,
                shippingAddress.Country, shippingAddress.ZipCode);
        var order = new Order(buyerId, address, lines);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);

        await NotifyActiveContactsAsync(order, NotificationKind.OrderPlaced,
            $"Your eShop order #{order.Id} has been placed.", null, cancellationToken);
        return ServiceResult<OrderView>.Success(await BuildOrderViewAsync(order, cancellationToken));
    }

    public async Task<ServiceResult<OrderView>> DispatchOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await FindOrderAsync(orderId, cancellationToken);
        if (order is null) return ServiceResult<OrderView>.Fail(ServiceFailure.NotFound, "Order was not found.");
        if (!order.Dispatch(Now))
            return ServiceResult<OrderView>.Fail(ServiceFailure.Conflict, "Only a placed order can be dispatched.");
        await _db.SaveChangesAsync(cancellationToken);

        await NotifyActiveContactsAsync(order, NotificationKind.OrderDispatched,
            $"Your eShop order #{order.Id} is on its way.", null, cancellationToken);
        await NotifyActiveContactsAsync(order, NotificationKind.DeliveryFollowUp,
            $"How did delivery of your eShop order #{order.Id} go?", Now.Add(FollowUpDelay), cancellationToken);
        return ServiceResult<OrderView>.Success(await BuildOrderViewAsync(order, cancellationToken));
    }

    public async Task<ServiceResult<OrderView>> CancelOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await FindOrderAsync(orderId, cancellationToken);
        if (order is null) return ServiceResult<OrderView>.Fail(ServiceFailure.NotFound, "Order was not found.");
        var newlyCancelled = order.Cancel(Now);
        if (newlyCancelled) await _db.SaveChangesAsync(cancellationToken);

        var scheduled = await _db.OrderNotifications.Where(x => x.OrderId == orderId &&
            x.Kind == NotificationKind.DeliveryFollowUp && x.ProviderMessageSid != null &&
            x.ProviderStatus != "canceled").ToListAsync(cancellationToken);
        await CancelScheduledNotificationsAsync(scheduled, cancellationToken);

        if (newlyCancelled)
            await NotifyActiveContactsAsync(order, NotificationKind.OrderCancelled,
                $"Your eShop order #{order.Id} has been cancelled.", null, cancellationToken);
        return ServiceResult<OrderView>.Success(await BuildOrderViewAsync(order, cancellationToken));
    }

    private async Task CancelScheduledNotificationsAsync(IReadOnlyList<OrderNotification> scheduled,
        CancellationToken cancellationToken)
    {
        foreach (var notification in scheduled)
        {
            try
            {
                var provider = await _twilio.CancelMessageAsync(notification.ProviderMessageSid!, cancellationToken);
                Apply(notification, provider);
            }
            catch (TwilioProviderException)
            {
                notification.MarkProviderFailure();
            }
            catch (HttpRequestException)
            {
                notification.MarkProviderFailure();
            }
            catch (InvalidOperationException)
            {
                notification.MarkProviderFailure();
            }
        }
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OrderView>> GetOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        var orders = await _db.Orders.Include(x => x.OrderItems).AsNoTracking()
            .Where(x => x.BuyerId == buyerId).OrderByDescending(x => x.OrderDate).ToListAsync(cancellationToken);
        await RefreshNotificationsAsync(orders.Select(x => x.Id).ToList(), cancellationToken);
        var results = new List<OrderView>();
        foreach (var order in orders) results.Add(await BuildOrderViewAsync(order, cancellationToken));
        return results;
    }

    public async Task<ServiceResult<IReadOnlyList<NotificationView>>> GetNotificationsAsync(string buyerId,
        int orderId, CancellationToken cancellationToken)
    {
        if (!await _db.Orders.AnyAsync(x => x.Id == orderId && x.BuyerId == buyerId, cancellationToken))
            return ServiceResult<IReadOnlyList<NotificationView>>.Fail(ServiceFailure.NotFound, "Order was not found.");
        await RefreshNotificationsAsync(new[] { orderId }, cancellationToken);
        var notifications = await _db.OrderNotifications.AsNoTracking().Where(x => x.OrderId == orderId)
            .OrderBy(x => x.CreatedAt).ToListAsync(cancellationToken);
        return ServiceResult<IReadOnlyList<NotificationView>>.Success(notifications.Select(ToView).ToList());
    }

    public async Task<ServiceResult<NotificationView>> ResendAsync(int notificationId, string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 200)
            return ServiceResult<NotificationView>.Fail(ServiceFailure.Invalid,
                "A non-empty idempotencyKey of at most 200 characters is required.");
        var gate = _resendCoordinator.For(notificationId, idempotencyKey);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var prior = await _db.NotificationResends.AsNoTracking().FirstOrDefaultAsync(x =>
                x.SourceNotificationId == notificationId && x.IdempotencyKey == idempotencyKey, cancellationToken);
            if (prior?.ResultNotificationId is int resultId)
            {
                var priorResult = await _db.OrderNotifications.AsNoTracking().SingleAsync(x => x.Id == resultId, cancellationToken);
                return ServiceResult<NotificationView>.Success(ToView(priorResult));
            }
            if (prior is not null)
                return ServiceResult<NotificationView>.Fail(ServiceFailure.Conflict, "The resend is already in progress.");

            var source = await _db.OrderNotifications.SingleOrDefaultAsync(x => x.Id == notificationId, cancellationToken);
            if (source is null)
                return ServiceResult<NotificationView>.Fail(ServiceFailure.NotFound, "Notification was not found.");
            if (source.ProviderMessageSid is not null) await RefreshNotificationAsync(source, cancellationToken);
            if (source.ProviderStatus is not ("failed" or "undelivered" or "provider-error"))
                return ServiceResult<NotificationView>.Fail(ServiceFailure.Conflict,
                    "Only a notification that failed to reach the shopper can be resent.");
            if (string.IsNullOrWhiteSpace(source.Content))
                return ServiceResult<NotificationView>.Fail(ServiceFailure.Conflict, "Deleted content cannot be resent.");
            if (!await _db.ContactNumbers.AnyAsync(x => x.Id == source.ContactNumberId && x.RemovedAt == null,
                    cancellationToken))
                return ServiceResult<NotificationView>.Fail(ServiceFailure.Conflict,
                    "The destination contact number has been removed.");

            var reservation = new NotificationResend(notificationId, idempotencyKey, Now);
            _db.NotificationResends.Add(reservation);
            await _db.SaveChangesAsync(cancellationToken);

            var resend = new OrderNotification(source.OrderId, source.BuyerId, source.ContactNumberId,
                source.Destination, source.Kind, source.Content, Now, null, source.Id);
            _db.OrderNotifications.Add(resend);
            await _db.SaveChangesAsync(cancellationToken);
            await SendAsync(resend, null, cancellationToken);
            reservation.Complete(resend.Id);
            await _db.SaveChangesAsync(cancellationToken);
            return ServiceResult<NotificationView>.Success(ToView(resend));
        }
        finally { gate.Release(); }
    }

    public async Task<ServiceResult<bool>> DeleteContentAsync(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _db.OrderNotifications.SingleOrDefaultAsync(x => x.Id == notificationId, cancellationToken);
        if (notification is null)
            return ServiceResult<bool>.Fail(ServiceFailure.NotFound, "Notification was not found.");
        if (notification.ContentDeletedAt is not null) return ServiceResult<bool>.Success(true);
        if (notification.ProviderMessageSid is not null)
        {
            try
            {
                var provider = await _twilio.RedactMessageAsync(notification.ProviderMessageSid, cancellationToken);
                Apply(notification, provider);
            }
            catch (TwilioProviderException)
            {
                return ServiceResult<bool>.Fail(ServiceFailure.ProviderUnavailable,
                    "The provider did not confirm content deletion.");
            }
            catch (HttpRequestException)
            {
                return ServiceResult<bool>.Fail(ServiceFailure.ProviderUnavailable,
                    "The provider did not confirm content deletion.");
            }
        }
        notification.MarkContentDeleted(Now);
        await _db.SaveChangesAsync(cancellationToken);
        return ServiceResult<bool>.Success(true);
    }

    public async Task<ServiceResult<ReconciliationView>> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (to < from)
            return ServiceResult<ReconciliationView>.Fail(ServiceFailure.Invalid, "to must not be before from.");
        IReadOnlyList<ProviderMessage> provider;
        try { provider = await _twilio.ListMessagesAsync(from, to, cancellationToken); }
        catch (TwilioProviderException)
        {
            return ServiceResult<ReconciliationView>.Fail(ServiceFailure.ProviderUnavailable,
                "The provider reconciliation query failed.");
        }
        catch (HttpRequestException)
        {
            return ServiceResult<ReconciliationView>.Fail(ServiceFailure.ProviderUnavailable,
                "The provider reconciliation query failed.");
        }
        var local = await _db.OrderNotifications.AsNoTracking()
            .Where(x => x.CreatedAt >= from && x.CreatedAt <= to)
            .ToListAsync(cancellationToken);
        var providerBySid = provider.Where(x => !string.IsNullOrWhiteSpace(x.Sid)).ToDictionary(x => x.Sid);
        var localBySid = local.Where(x => x.ProviderMessageSid != null).ToDictionary(x => x.ProviderMessageSid!);
        var ids = providerBySid.Keys.Union(localBySid.Keys).OrderBy(x => x);
        var rows = ids.Select(sid =>
        {
            providerBySid.TryGetValue(sid, out var p);
            localBySid.TryGetValue(sid, out var l);
            return new ReconciliationItemView(sid, p is not null, l is not null, l?.Id, l?.OrderId,
                p?.Status, l?.ProviderStatus, p?.DateSent ?? p?.DateCreated);
        }).ToList();
        rows.AddRange(local.Where(x => x.ProviderMessageSid is null).Select(x =>
            new ReconciliationItemView(null, false, true, x.Id, x.OrderId, null, x.ProviderStatus, x.CreatedAt)));
        return ServiceResult<ReconciliationView>.Success(new ReconciliationView(from, to, rows));
    }

    private DateTimeOffset Now => _clock.GetUtcNow();

    private async Task NotifyActiveContactsAsync(Order order, NotificationKind kind, string content,
        DateTimeOffset? sendAt, CancellationToken cancellationToken)
    {
        var contacts = await _db.ContactNumbers.Where(x => x.BuyerId == order.BuyerId && x.RemovedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var contact in contacts)
        {
            var notification = new OrderNotification(order.Id, order.BuyerId, contact.Id,
                contact.CanonicalNumber, kind, content, Now, sendAt);
            _db.OrderNotifications.Add(notification);
            await _db.SaveChangesAsync(cancellationToken);
            await SendAsync(notification, sendAt, cancellationToken);
        }
    }

    private async Task SendAsync(OrderNotification notification, DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var provider = await _twilio.SendMessageAsync(notification.Destination, notification.Content!, sendAt,
                cancellationToken);
            Apply(notification, provider);
        }
        catch (TwilioProviderException ex) { notification.MarkProviderFailure(ex.ProviderCode); }
        catch (InvalidOperationException) { notification.MarkProviderFailure(); }
        catch (HttpRequestException) { notification.MarkProviderFailure(); }
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task RefreshNotificationsAsync(IReadOnlyCollection<int> orderIds, CancellationToken cancellationToken)
    {
        var notifications = await _db.OrderNotifications.Where(x => orderIds.Contains(x.OrderId) &&
            x.ProviderMessageSid != null).ToListAsync(cancellationToken);
        foreach (var notification in notifications) await RefreshNotificationAsync(notification, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task RefreshNotificationAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        try { Apply(notification, await _twilio.FetchMessageAsync(notification.ProviderMessageSid!, cancellationToken)); }
        catch (TwilioProviderException) { }
        catch (HttpRequestException) { }
    }

    private static void Apply(OrderNotification notification, ProviderMessage provider) =>
        notification.ApplyProviderState(provider.Sid, provider.Status, provider.ErrorCode,
            provider.ErrorMessage, provider.DateSent, provider.DateUpdated);

    private Task<Order?> FindOrderAsync(int id, CancellationToken cancellationToken) =>
        _db.Orders.Include(x => x.OrderItems).SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    private async Task<OrderView> BuildOrderViewAsync(Order order, CancellationToken cancellationToken)
    {
        if (!_db.Entry(order).Collection(x => x.OrderItems).IsLoaded)
            await _db.Entry(order).Collection(x => x.OrderItems).LoadAsync(cancellationToken);
        var notifications = await _db.OrderNotifications.AsNoTracking().Where(x => x.OrderId == order.Id)
            .OrderBy(x => x.CreatedAt).ToListAsync(cancellationToken);
        return new OrderView(order.Id, order.Status, order.OrderDate, order.Total(),
            order.OrderItems.Select(x => new OrderLineView(x.ItemOrdered.CatalogItemId,
                x.ItemOrdered.ProductName, x.UnitPrice, x.Units)).ToList(),
            notifications.Select(x => new NotificationSummaryView(x.Id, x.Kind, x.ProviderStatus,
                x.ProviderMessageSid, x.ScheduledFor)).ToList());
    }

    private static ContactNumberView ToView(ContactNumber x) => new(x.Id, x.CanonicalNumber, x.CreatedAt);
    private static NotificationView ToView(OrderNotification x) => new(x.Id, x.OrderId, x.Kind, x.Content,
        x.ContentDeletedAt is not null, x.ProviderMessageSid, x.ProviderStatus, x.ProviderErrorCode,
        x.ProviderErrorMessage, x.CreatedAt, x.ScheduledFor, x.ProviderSentAt, x.ProviderUpdatedAt,
        x.OriginalNotificationId);
}
