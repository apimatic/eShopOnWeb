using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

public sealed class OrderNotificationService
{
    private static readonly SemaphoreSlim ResendLock = new(1, 1);
    private readonly CatalogContext _db;
    private readonly ITwilioClient _twilio;
    private readonly TimeProvider _timeProvider;

    public OrderNotificationService(CatalogContext db, ITwilioClient twilio, TimeProvider timeProvider)
    {
        _db = db;
        _twilio = twilio;
        _timeProvider = timeProvider;
    }

    public async Task<RegisterContactNumberResponse> RegisterContactNumberAsync(string buyerId,
        RegisterContactNumberRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PhoneNumber) || request.PhoneNumber.Length > 100)
            throw new ApiProblemException(400, "A phone number is required.");

        ValidatedPhoneNumber lookup;
        try
        {
            lookup = await _twilio.ValidatePhoneNumberAsync(request.PhoneNumber.Trim(), cancellationToken);
        }
        catch (TwilioRequestException)
        {
            throw new ApiProblemException(503, "The phone number could not be validated right now.");
        }
        catch (TwilioConfigurationException)
        {
            throw new ApiProblemException(503, "Phone-number validation is not configured.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            throw new ApiProblemException(503, "The phone number could not be validated right now.");
        }

        if (!lookup.IsValid || string.IsNullOrWhiteSpace(lookup.CanonicalNumber))
            throw new ApiProblemException(400, "The phone number is not a valid messaging destination.");

        var exists = await _db.ContactNumbers.AnyAsync(x => x.BuyerId == buyerId &&
            x.E164Number == lookup.CanonicalNumber && x.DeletedAt == null, cancellationToken);
        if (exists) throw new ApiProblemException(409, "That contact number is already registered.");

        var contact = new ContactNumber(buyerId, lookup.CanonicalNumber, UtcNow());
        _db.ContactNumbers.Add(contact);
        await _db.SaveChangesAsync(cancellationToken);
        return new RegisterContactNumberResponse(contact.Id, contact.E164Number);
    }

    public async Task<ContactNumbersResponse> GetContactNumbersAsync(string buyerId, CancellationToken cancellationToken)
    {
        var values = await _db.ContactNumbers.AsNoTracking()
            .Where(x => x.BuyerId == buyerId && x.DeletedAt == null)
            .OrderBy(x => x.Id)
            .Select(x => new ContactNumberDto(x.Id, x.E164Number, x.CreatedAt))
            .ToListAsync(cancellationToken);
        return new ContactNumbersResponse(values);
    }

    public async Task DeleteContactNumberAsync(string buyerId, int contactNumberId,
        CancellationToken cancellationToken)
    {
        var contact = await _db.ContactNumbers.SingleOrDefaultAsync(x => x.Id == contactNumberId &&
            x.BuyerId == buyerId && x.DeletedAt == null, cancellationToken)
            ?? throw new ApiProblemException(404, "Contact number not found.");

        var scheduled = await _db.OrderNotifications.Where(x => x.ContactNumberId == contact.Id &&
            (x.ProviderStatus == "scheduled" || x.CancellationPending)).ToListAsync(cancellationToken);
        foreach (var notification in scheduled)
        {
            if (!await TryCancelScheduledAsync(notification, cancellationToken))
                throw new ApiProblemException(502, "A queued notification could not be cancelled; the contact number was not removed.");
        }

        contact.Delete(UtcNow());
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<CreateOrderResponse> CreateOrderAsync(string buyerId, CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        ValidateOrderRequest(request);
        var requestedIds = request.Items.Select(x => x.CatalogItemId).Distinct().ToList();
        if (requestedIds.Count != request.Items.Count)
            throw new ApiProblemException(400, "Each catalog item may appear only once.");

        var catalogItems = await _db.CatalogItems.Where(x => requestedIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        if (catalogItems.Count != requestedIds.Count)
            throw new ApiProblemException(400, "One or more catalog items do not exist.");

        var orderItems = request.Items.Select(item =>
        {
            var catalogItem = catalogItems[item.CatalogItemId];
            var ordered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri);
            return new OrderItem(ordered, catalogItem.Price, item.Quantity);
        }).ToList();
        var address = new Address(request.ShippingAddress.Street, request.ShippingAddress.City,
            request.ShippingAddress.State, request.ShippingAddress.Country, request.ShippingAddress.ZipCode);
        var order = new Order(buyerId, address, orderItems);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);

        await NotifyActiveContactsAsync(order, NotificationKind.OrderPlaced,
            $"eShopOnWeb: Order {order.Id} was placed. Total {order.Total().ToString("0.00", CultureInfo.InvariantCulture)}.",
            null, cancellationToken);
        return new CreateOrderResponse(order.Id);
    }

    public async Task<OrderActionResponse> DispatchOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        if (order.Status == OrderStatus.Cancelled)
            throw new ApiProblemException(409, "A cancelled order cannot be dispatched.");
        if (order.Status == OrderStatus.Dispatched)
            return new OrderActionResponse(order.Id, order.Status.ToString());

        var now = UtcNow();
        order.Dispatch(now);
        await _db.SaveChangesAsync(cancellationToken);
        await NotifyActiveContactsAsync(order, NotificationKind.OrderDispatched,
            $"eShopOnWeb: Order {order.Id} has been dispatched and is on its way.", null, cancellationToken);
        await NotifyActiveContactsAsync(order, NotificationKind.DeliveryFollowUp,
            $"eShopOnWeb: How did delivery of order {order.Id} go?", now.AddDays(3), cancellationToken);
        return new OrderActionResponse(order.Id, order.Status.ToString());
    }

    public async Task<OrderActionResponse> CancelOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        if (order.Status == OrderStatus.Cancelled)
            return new OrderActionResponse(order.Id, order.Status.ToString());

        order.Cancel(UtcNow());
        var followUps = await _db.OrderNotifications.Where(x => x.OrderId == order.Id &&
            x.Kind == NotificationKind.DeliveryFollowUp && x.ProviderStatus == "scheduled").ToListAsync(cancellationToken);
        foreach (var followUp in followUps) followUp.RequestCancellation();
        await _db.SaveChangesAsync(cancellationToken);

        foreach (var followUp in followUps)
        {
            await TryCancelScheduledAsync(followUp, cancellationToken);
        }
        await SaveWithoutFailingOperationAsync(cancellationToken);

        await NotifyActiveContactsAsync(order, NotificationKind.OrderCancelled,
            $"eShopOnWeb: Order {order.Id} has been cancelled.", null, cancellationToken);
        return new OrderActionResponse(order.Id, order.Status.ToString());
    }

    public async Task<MyOrdersResponse> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        var orders = await _db.Orders.AsNoTracking().Include(x => x.OrderItems)
            .Where(x => x.BuyerId == buyerId).OrderByDescending(x => x.OrderDate).ToListAsync(cancellationToken);
        await RefreshNotificationsAsync(_db.OrderNotifications.Where(x => x.BuyerId == buyerId), cancellationToken);
        var notifications = await _db.OrderNotifications.AsNoTracking().Where(x => x.BuyerId == buyerId)
            .OrderBy(x => x.Id).ToListAsync(cancellationToken);
        var response = orders.Select(order => new OrderDto(order.Id, order.OrderDate, order.Status.ToString(), order.Total(),
            order.OrderItems.Select(item => new OrderItemDto(item.ItemOrdered.CatalogItemId,
                item.ItemOrdered.ProductName, item.UnitPrice, item.Units)).ToList(),
            notifications.Where(x => x.OrderId == order.Id).Select(ToSummary).ToList())).ToList();
        return new MyOrdersResponse(response);
    }

    public async Task<OrderNotificationsResponse> GetOrderNotificationsAsync(string buyerId, int orderId,
        CancellationToken cancellationToken)
    {
        var ownsOrder = await _db.Orders.AnyAsync(x => x.Id == orderId && x.BuyerId == buyerId, cancellationToken);
        if (!ownsOrder) throw new ApiProblemException(404, "Order not found.");
        await RefreshNotificationsAsync(_db.OrderNotifications.Where(x => x.OrderId == orderId), cancellationToken);
        var values = await _db.OrderNotifications.AsNoTracking().Where(x => x.OrderId == orderId)
            .OrderBy(x => x.Id).ToListAsync(cancellationToken);
        return new OrderNotificationsResponse(orderId, values.Select(ToDto).ToList());
    }

    public async Task<ResendNotificationResponse> ResendAsync(int notificationId, string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 200)
            throw new ApiProblemException(400, "An idempotency key of at most 200 characters is required.");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(idempotencyKey)));

        await ResendLock.WaitAsync(cancellationToken);
        try
        {
            var prior = await _db.NotificationResendRequests.AsNoTracking().SingleOrDefaultAsync(x =>
                x.OriginalNotificationId == notificationId && x.IdempotencyKeyHash == hash, cancellationToken);
            if (prior != null) return new ResendNotificationResponse(prior.NotificationId);

            var original = await _db.OrderNotifications.SingleOrDefaultAsync(x => x.Id == notificationId, cancellationToken)
                ?? throw new ApiProblemException(404, "Notification not found.");
            await RefreshNotificationAsync(original, cancellationToken);
            if (original.ProviderStatus is not ("failed" or "undelivered" or "provider-error"))
                throw new ApiProblemException(409, "Only a notification that failed or was undelivered can be resent.");
            if (original.Body == null)
                throw new ApiProblemException(409, "Disposed notification content cannot be resent.");

            var contact = await _db.ContactNumbers.SingleOrDefaultAsync(x => x.Id == original.ContactNumberId &&
                x.DeletedAt == null, cancellationToken)
                ?? throw new ApiProblemException(409, "The notification's contact number is no longer active.");
            var order = await GetOrderAsync(original.OrderId, cancellationToken);
            if (order.Status == OrderStatus.Cancelled && original.Kind == NotificationKind.DeliveryFollowUp)
                throw new ApiProblemException(409, "A delivery follow-up for a cancelled order cannot be resent.");

            var resend = new OrderNotification(original.OrderId, original.BuyerId, contact.Id,
                original.Kind, original.Body, UtcNow(), originalNotificationId: original.Id);
            _db.OrderNotifications.Add(resend);
            await _db.SaveChangesAsync(cancellationToken);
            _db.NotificationResendRequests.Add(new NotificationResendRequest(original.Id, hash, resend.Id, UtcNow()));
            await _db.SaveChangesAsync(cancellationToken);
            await SendRecordedNotificationAsync(resend, contact.E164Number, null, cancellationToken);
            return new ResendNotificationResponse(resend.Id);
        }
        finally
        {
            ResendLock.Release();
        }
    }

    public async Task DisposeContentAsync(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _db.OrderNotifications.SingleOrDefaultAsync(x => x.Id == notificationId, cancellationToken)
            ?? throw new ApiProblemException(404, "Notification not found.");
        if (notification.Body == null) return;

        if (notification.ProviderMessageSid != null)
        {
            try
            {
                if (notification.ProviderStatus == "scheduled")
                {
                    var cancelled = await _twilio.CancelMessageAsync(notification.ProviderMessageSid, cancellationToken);
                    ApplyProviderState(notification, cancelled);
                }
                var redacted = await _twilio.RedactMessageAsync(notification.ProviderMessageSid, cancellationToken);
                ApplyProviderState(notification, redacted);
            }
            catch (TwilioRequestException)
            {
                throw new ApiProblemException(502, "Twilio did not confirm that the message content was disposed.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                throw new ApiProblemException(502, "Twilio did not confirm that the message content was disposed.");
            }
        }

        notification.DisposeContent(UtcNow());
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<ReconciliationResponse> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (to < from) throw new ApiProblemException(400, "'to' must be on or after 'from'.");
        IReadOnlyList<TwilioMessage> provider;
        try
        {
            provider = await _twilio.ListMessagesAsync(from, to, cancellationToken);
        }
        catch (TwilioRequestException)
        {
            throw new ApiProblemException(502, "Twilio reconciliation could not be completed.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            throw new ApiProblemException(502, "Twilio reconciliation could not be completed.");
        }

        var providerBySid = provider.GroupBy(x => x.Sid).ToDictionary(x => x.Key, x => x.Last());
        var local = await _db.OrderNotifications.AsNoTracking()
            .Where(x => x.CreatedAt >= from && x.CreatedAt <= to && x.ProviderMessageSid != null)
            .ToListAsync(cancellationToken);
        var localBySid = local.ToDictionary(x => x.ProviderMessageSid!, x => x);
        var sids = providerBySid.Keys.Union(localBySid.Keys, StringComparer.Ordinal).OrderBy(x => x).ToList();
        var entries = sids.Select(sid =>
        {
            providerBySid.TryGetValue(sid, out var remote);
            localBySid.TryGetValue(sid, out var own);
            return new ReconciliationEntry(sid, own?.Id, own != null, remote != null,
                own?.ProviderStatus, remote?.Status, remote?.DateCreated, remote?.DateSent);
        }).ToList();
        return new ReconciliationResponse(from, to,
            entries.Count(x => x.ExistsInEshop && x.ExistsInProvider),
            entries.Count(x => !x.ExistsInEshop && x.ExistsInProvider),
            entries.Count(x => x.ExistsInEshop && !x.ExistsInProvider), entries);
    }

    public async Task RetryPendingCancellationsAsync(CancellationToken cancellationToken)
    {
        var pending = await _db.OrderNotifications.Where(x => x.CancellationPending).ToListAsync(cancellationToken);
        foreach (var notification in pending) await TryCancelScheduledAsync(notification, cancellationToken);
        await SaveWithoutFailingOperationAsync(cancellationToken);
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken) =>
        await _db.Orders.Include(x => x.OrderItems).SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken)
        ?? throw new ApiProblemException(404, "Order not found.");

    private async Task NotifyActiveContactsAsync(Order order, NotificationKind kind, string body,
        DateTimeOffset? scheduledFor, CancellationToken cancellationToken)
    {
        var contacts = await _db.ContactNumbers.Where(x => x.BuyerId == order.BuyerId && x.DeletedAt == null)
            .OrderBy(x => x.Id).ToListAsync(cancellationToken);
        foreach (var contact in contacts)
        {
            var notification = new OrderNotification(order.Id, order.BuyerId, contact.Id, kind, body, UtcNow(), scheduledFor);
            _db.OrderNotifications.Add(notification);
            try { await _db.SaveChangesAsync(cancellationToken); }
            catch { continue; }
            await SendRecordedNotificationAsync(notification, contact.E164Number, scheduledFor, cancellationToken);
        }
    }

    private async Task SendRecordedNotificationAsync(OrderNotification notification, string destination,
        DateTimeOffset? scheduledFor, CancellationToken cancellationToken)
    {
        try
        {
            var sent = await _twilio.SendMessageAsync(destination, notification.Body!, scheduledFor, cancellationToken);
            ApplyProviderState(notification, sent);
        }
        catch (TwilioRequestException ex)
        {
            notification.RecordProviderFailure(ex.ProviderErrorCode);
        }
        catch (TwilioConfigurationException)
        {
            notification.RecordProviderFailure(null);
        }
        catch (HttpRequestException)
        {
            notification.RecordProviderFailure(null);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            notification.RecordProviderFailure(null);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            notification.RecordProviderFailure(null);
        }
        await SaveWithoutFailingOperationAsync(cancellationToken);
    }

    private async Task RefreshNotificationsAsync(IQueryable<OrderNotification> query, CancellationToken cancellationToken)
    {
        var notifications = await query.Where(x => x.ProviderMessageSid != null).ToListAsync(cancellationToken);
        foreach (var notification in notifications) await RefreshNotificationAsync(notification, cancellationToken);
        await SaveWithoutFailingOperationAsync(cancellationToken);
    }

    private async Task RefreshNotificationAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        if (notification.ProviderMessageSid == null) return;
        try
        {
            ApplyProviderState(notification, await _twilio.GetMessageAsync(notification.ProviderMessageSid, cancellationToken));
        }
        catch (TwilioRequestException) { }
        catch (HttpRequestException) { }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { }
        catch (Exception) when (!cancellationToken.IsCancellationRequested) { }
    }

    private async Task<bool> TryCancelScheduledAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        if (notification.ProviderMessageSid == null) return true;
        try
        {
            var current = await _twilio.GetMessageAsync(notification.ProviderMessageSid, cancellationToken);
            ApplyProviderState(notification, current);
            if (current.Status == "scheduled")
            {
                ApplyProviderState(notification,
                    await _twilio.CancelMessageAsync(notification.ProviderMessageSid, cancellationToken));
            }
            return notification.ProviderStatus != "scheduled";
        }
        catch (TwilioRequestException)
        {
            notification.RequestCancellation();
            return false;
        }
        catch (HttpRequestException)
        {
            notification.RequestCancellation();
            return false;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            notification.RequestCancellation();
            return false;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            notification.RequestCancellation();
            return false;
        }
    }

    private static void ApplyProviderState(OrderNotification notification, TwilioMessage message) =>
        notification.RecordProviderState(message.Sid, message.Status, message.ErrorCode,
            message.DateCreated, message.DateSent);

    private async Task SaveWithoutFailingOperationAsync(CancellationToken cancellationToken)
    {
        try { await _db.SaveChangesAsync(cancellationToken); }
        catch { }
    }

    private DateTimeOffset UtcNow() => _timeProvider.GetUtcNow();

    private static void ValidateOrderRequest(CreateOrderRequest request)
    {
        if (request.Items == null || request.Items.Count == 0)
            throw new ApiProblemException(400, "At least one catalog item is required.");
        if (request.Items.Any(x => x.CatalogItemId <= 0 || x.Quantity <= 0))
            throw new ApiProblemException(400, "Catalog item ids and quantities must be positive.");
        var address = request.ShippingAddress;
        if (address == null || string.IsNullOrWhiteSpace(address.Street) || string.IsNullOrWhiteSpace(address.City) ||
            string.IsNullOrWhiteSpace(address.Country) || string.IsNullOrWhiteSpace(address.ZipCode))
            throw new ApiProblemException(400, "A complete shipping address is required.");
    }

    private static NotificationSummaryDto ToSummary(OrderNotification value) => new(value.Id,
        value.Kind.ToString(), value.ProviderStatus, value.ProviderErrorCode, value.ScheduledFor,
        value.ProviderDateSent, value.ContentDisposedAt != null);

    private static NotificationDto ToDto(OrderNotification value) => new(value.Id, value.OrderId,
        value.Kind.ToString(), value.ProviderStatus, value.ProviderMessageSid, value.ProviderErrorCode,
        value.Body, value.CreatedAt, value.ScheduledFor, value.ProviderDateSent,
        value.ContentDisposedAt != null, value.OriginalNotificationId);
}
