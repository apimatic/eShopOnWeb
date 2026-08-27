using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.PublicApi.OrderNotifications;

public sealed class OrderNotificationService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ResendLocks = new();
    private readonly CatalogContext _db;
    private readonly ITwilioMessagingClient _twilio;
    private readonly TimeProvider _timeProvider;

    public OrderNotificationService(CatalogContext db, ITwilioMessagingClient twilio,
        TimeProvider timeProvider)
    {
        _db = db;
        _twilio = twilio;
        _timeProvider = timeProvider;
    }

    public async Task<RegisterContactNumberResponse> RegisterContactNumberAsync(string buyerId,
        RegisterContactNumberRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            throw new NotificationApiException((int)HttpStatusCode.BadRequest,
                "A phone number is required.");
        }

        ValidatedPhoneNumber validation;
        try
        {
            validation = await _twilio.ValidatePhoneNumberAsync(request.PhoneNumber,
                request.CountryCode, cancellationToken);
        }
        catch (TwilioRequestException)
        {
            throw new NotificationApiException((int)HttpStatusCode.BadGateway,
                "The phone number could not be validated by the messaging provider.");
        }
        catch (HttpRequestException)
        {
            throw new NotificationApiException((int)HttpStatusCode.BadGateway,
                "The phone number could not be validated by the messaging provider.");
        }
        catch (TaskCanceledException)
        {
            throw new NotificationApiException((int)HttpStatusCode.GatewayTimeout,
                "The phone number validation request timed out.");
        }

        if (!validation.IsValid || string.IsNullOrWhiteSpace(validation.PhoneNumber))
        {
            throw new NotificationApiException((int)HttpStatusCode.BadRequest,
                "The messaging provider does not consider this a valid destination.");
        }

        var exists = await _db.ContactNumbers.AnyAsync(x => x.BuyerId == buyerId &&
            x.PhoneNumber == validation.PhoneNumber, cancellationToken);
        if (exists)
        {
            throw new NotificationApiException((int)HttpStatusCode.Conflict,
                "This contact number is already registered.");
        }

        var contact = new ContactNumber(buyerId, validation.PhoneNumber,
            _timeProvider.GetUtcNow());
        _db.ContactNumbers.Add(contact);
        await _db.SaveChangesAsync(cancellationToken);
        return new RegisterContactNumberResponse(contact.Id, contact.PhoneNumber);
    }

    public async Task<ContactNumberListResponse> GetContactNumbersAsync(string buyerId,
        CancellationToken cancellationToken)
    {
        var numbers = await _db.ContactNumbers.AsNoTracking()
            .Where(x => x.BuyerId == buyerId)
            .OrderBy(x => x.Id)
            .Select(x => new ContactNumberDto(x.Id, x.PhoneNumber, x.CreatedAt))
            .ToListAsync(cancellationToken);
        return new ContactNumberListResponse(numbers);
    }

    public async Task DeleteContactNumberAsync(string buyerId, int contactNumberId,
        CancellationToken cancellationToken)
    {
        var contact = await _db.ContactNumbers.SingleOrDefaultAsync(
            x => x.Id == contactNumberId && x.BuyerId == buyerId, cancellationToken);
        if (contact is null)
        {
            throw new NotificationApiException((int)HttpStatusCode.NotFound,
                "Contact number not found.");
        }

        var linked = await _db.OrderNotifications
            .Where(x => x.ContactNumberId == contact.Id)
            .ToListAsync(cancellationToken);

        foreach (var notification in linked.Where(IsPotentiallyScheduled))
        {
            if (!await CancelScheduledProviderMessageAsync(notification, cancellationToken))
            {
                await _db.SaveChangesAsync(CancellationToken.None);
                throw new NotificationApiException((int)HttpStatusCode.ServiceUnavailable,
                    "The contact number was not removed because a scheduled message could not be cancelled.");
            }
        }

        foreach (var notification in linked)
        {
            notification.DetachContactNumber();
        }
        _db.ContactNumbers.Remove(contact);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<PlaceOrderResponse> PlaceOrderAsync(string buyerId,
        PlaceOrderRequest request, CancellationToken cancellationToken)
    {
        if (request.Items is null || request.Items.Count == 0 ||
            request.Items.Any(x => x.CatalogItemId <= 0 || x.Quantity <= 0))
        {
            throw new NotificationApiException((int)HttpStatusCode.BadRequest,
                "At least one catalog item with a positive quantity is required.");
        }

        var requested = request.Items
            .GroupBy(x => x.CatalogItemId)
            .ToDictionary(x => x.Key, x => x.Sum(i => i.Quantity));
        var catalogItems = await _db.CatalogItems
            .Where(x => requested.Keys.Contains(x.Id))
            .ToListAsync(cancellationToken);
        if (catalogItems.Count != requested.Count)
        {
            throw new NotificationApiException((int)HttpStatusCode.BadRequest,
                "One or more catalog items do not exist.");
        }

        var items = catalogItems.Select(item => new OrderItem(
            new CatalogItemOrdered(item.Id, item.Name, item.PictureUri),
            item.Price,
            requested[item.Id])).ToList();
        var address = request.ShippingAddress is null
            ? new Address("Not provided", "Not provided", "", "Not provided", "Not provided")
            : new Address(request.ShippingAddress.Street, request.ShippingAddress.City,
                request.ShippingAddress.State, request.ShippingAddress.Country,
                request.ShippingAddress.ZipCode);
        var order = new Order(buyerId, address, items);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);

        await NotifyAllContactsAsync(order, NotificationKind.OrderPlaced,
            $"eShopOnWeb: Order {order.Id} has been placed.", null, cancellationToken);
        return new PlaceOrderResponse(order.Id);
    }

    public async Task<OrderStateResponse> DispatchOrderAsync(int orderId,
        CancellationToken cancellationToken)
    {
        var order = await FindOrderAsync(orderId, cancellationToken);
        bool changed;
        try
        {
            changed = order.Dispatch(_timeProvider.GetUtcNow());
        }
        catch (InvalidOperationException ex)
        {
            throw new NotificationApiException((int)HttpStatusCode.Conflict, ex.Message);
        }

        if (changed)
        {
            await _db.SaveChangesAsync(cancellationToken);
            await NotifyAllContactsAsync(order, NotificationKind.OrderDispatched,
                $"eShopOnWeb: Order {order.Id} is on its way.", null, cancellationToken);
            await NotifyAllContactsAsync(order, NotificationKind.DeliveryFollowUp,
                $"eShopOnWeb: How did delivery of order {order.Id} go?",
                _timeProvider.GetUtcNow().AddDays(3), cancellationToken);
        }

        return new OrderStateResponse(order.Id, order.Status.ToString());
    }

    public async Task<OrderStateResponse> CancelOrderAsync(int orderId,
        CancellationToken cancellationToken)
    {
        var order = await FindOrderAsync(orderId, cancellationToken);
        var changed = order.Cancel(_timeProvider.GetUtcNow());
        if (changed)
        {
            await _db.SaveChangesAsync(cancellationToken);
            var scheduled = await _db.OrderNotifications
                .Where(x => x.OrderId == order.Id && x.Kind == NotificationKind.DeliveryFollowUp)
                .ToListAsync(cancellationToken);
            foreach (var notification in scheduled.Where(IsPotentiallyScheduled))
            {
                await CancelScheduledProviderMessageAsync(notification, cancellationToken);
            }
            await _db.SaveChangesAsync(CancellationToken.None);

            await NotifyAllContactsAsync(order, NotificationKind.OrderCancelled,
                $"eShopOnWeb: Order {order.Id} has been cancelled.", null, cancellationToken);
        }

        return new OrderStateResponse(order.Id, order.Status.ToString());
    }

    public async Task<MyOrdersResponse> GetMyOrdersAsync(string buyerId,
        CancellationToken cancellationToken)
    {
        var orders = await _db.Orders.AsNoTracking()
            .Include(x => x.OrderItems)
            .Where(x => x.BuyerId == buyerId)
            .OrderByDescending(x => x.OrderDate)
            .ToListAsync(cancellationToken);
        var notifications = await _db.OrderNotifications
            .Where(x => x.BuyerId == buyerId)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        await RefreshProviderStatesAsync(notifications, cancellationToken);

        return new MyOrdersResponse(orders.Select(order => new MyOrderDto(
            order.Id,
            order.OrderDate,
            order.Status.ToString(),
            order.Total(),
            notifications.Where(x => x.OrderId == order.Id).Select(Map).ToList())).ToList());
    }

    public async Task<NotificationListResponse> GetOrderNotificationsAsync(string buyerId,
        int orderId, CancellationToken cancellationToken)
    {
        var owned = await _db.Orders.AnyAsync(x => x.Id == orderId && x.BuyerId == buyerId,
            cancellationToken);
        if (!owned)
        {
            throw new NotificationApiException((int)HttpStatusCode.NotFound, "Order not found.");
        }

        var notifications = await _db.OrderNotifications
            .Where(x => x.OrderId == orderId && x.BuyerId == buyerId)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        await RefreshProviderStatesAsync(notifications, cancellationToken);
        return new NotificationListResponse(notifications.Select(Map).ToList());
    }

    public async Task<ResendNotificationResponse> ResendAsync(int notificationId,
        ResendNotificationRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 128)
        {
            throw new NotificationApiException((int)HttpStatusCode.BadRequest,
                "An idempotency key of no more than 128 characters is required.");
        }

        var lockKey = $"{notificationId}:{request.IdempotencyKey}";
        var gate = ResendLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var existing = await _db.OrderNotifications.SingleOrDefaultAsync(x =>
                x.OriginalNotificationId == notificationId &&
                x.ResendIdempotencyKey == request.IdempotencyKey, cancellationToken);
            if (existing is not null)
            {
                return new ResendNotificationResponse(existing.Id);
            }

            var original = await _db.OrderNotifications.SingleOrDefaultAsync(
                x => x.Id == notificationId, cancellationToken);
            if (original is null)
            {
                throw new NotificationApiException((int)HttpStatusCode.NotFound,
                    "Notification not found.");
            }

            await RefreshProviderStatesAsync(new[] { original }, cancellationToken);
            if (!IsUndelivered(original.ProviderStatus))
            {
                throw new NotificationApiException((int)HttpStatusCode.Conflict,
                    "Only failed or undelivered notifications can be resent.");
            }
            if (original.Body is null || original.ContentDisposedAt.HasValue ||
                !original.ContactNumberId.HasValue)
            {
                throw new NotificationApiException((int)HttpStatusCode.Conflict,
                    "This notification no longer has resendable content or a registered destination.");
            }

            var contact = await _db.ContactNumbers.SingleOrDefaultAsync(x =>
                x.Id == original.ContactNumberId.Value && x.BuyerId == original.BuyerId,
                cancellationToken);
            if (contact is null)
            {
                throw new NotificationApiException((int)HttpStatusCode.Conflict,
                    "The destination is no longer registered.");
            }

            var resend = new OrderNotification(original.OrderId, original.BuyerId, contact.Id,
                NotificationKind.Resend, original.Body, _timeProvider.GetUtcNow(), null,
                original.Id, request.IdempotencyKey);
            _db.OrderNotifications.Add(resend);
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                _db.Entry(resend).State = EntityState.Detached;
                existing = await _db.OrderNotifications.AsNoTracking().SingleAsync(x =>
                    x.OriginalNotificationId == notificationId &&
                    x.ResendIdempotencyKey == request.IdempotencyKey, cancellationToken);
                return new ResendNotificationResponse(existing.Id);
            }

            await SendNotificationAsync(resend, contact.PhoneNumber, cancellationToken);
            return new ResendNotificationResponse(resend.Id);
        }
        finally
        {
            gate.Release();
            ResendLocks.TryRemove(lockKey, out _);
        }
    }

    public async Task DisposeContentAsync(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _db.OrderNotifications.SingleOrDefaultAsync(
            x => x.Id == notificationId, cancellationToken);
        if (notification is null)
        {
            throw new NotificationApiException((int)HttpStatusCode.NotFound,
                "Notification not found.");
        }
        if (notification.ContentDisposedAt.HasValue)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(notification.ProviderSid))
        {
            try
            {
                var redacted = await _twilio.RedactMessageAsync(notification.ProviderSid,
                    cancellationToken);
                if (!string.IsNullOrEmpty(redacted.Body))
                {
                    throw new NotificationApiException((int)HttpStatusCode.BadGateway,
                        "The provider did not redact the message content.");
                }
            }
            catch (TwilioRequestException)
            {
                throw new NotificationApiException((int)HttpStatusCode.BadGateway,
                    "The provider did not redact the message content.");
            }
        }

        notification.DisposeContent(_timeProvider.GetUtcNow());
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<ReconciliationResponse> ReconcileAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken)
    {
        if (from >= to)
        {
            throw new NotificationApiException((int)HttpStatusCode.BadRequest,
                "The 'from' value must be earlier than 'to'.");
        }
        IReadOnlyList<ProviderMessage> providerMessages;
        try
        {
            providerMessages = await _twilio.ListMessagesAsync(from, to, cancellationToken);
        }
        catch (TwilioRequestException)
        {
            throw new NotificationApiException((int)HttpStatusCode.BadGateway,
                "The provider reconciliation query failed.");
        }

        providerMessages = providerMessages.Where(x =>
        {
            var timestamp = x.DateSent ?? x.DateCreated;
            return timestamp >= from && timestamp <= to;
        }).ToList();
        var providerSids = providerMessages.Select(x => x.Sid).ToList();
        var local = await _db.OrderNotifications
            .Where(x => (x.CreatedAt >= from && x.CreatedAt <= to) ||
                        (x.ProviderSid != null && providerSids.Contains(x.ProviderSid)))
            .ToListAsync(cancellationToken);
        var localBySid = local.Where(x => !string.IsNullOrWhiteSpace(x.ProviderSid))
            .ToDictionary(x => x.ProviderSid!, StringComparer.Ordinal);
        var providerBySid = providerMessages.ToDictionary(x => x.Sid, StringComparer.Ordinal);

        var matched = providerMessages.Where(x => localBySid.ContainsKey(x.Sid))
            .Select(x => ProviderEntry("matched", x, localBySid[x.Sid])).ToList();
        var providerOnly = providerMessages.Where(x => !localBySid.ContainsKey(x.Sid))
            .Select(x => ProviderEntry("providerOnly", x, null)).ToList();
        var applicationOnly = local.Where(x => x.CreatedAt >= from && x.CreatedAt <= to &&
                (string.IsNullOrWhiteSpace(x.ProviderSid) ||
                 !providerBySid.ContainsKey(x.ProviderSid)))
            .Select(x => new ReconciliationEntry("applicationOnly", x.ProviderSid, x.Id,
                x.OrderId, x.ProviderStatus, x.ProviderDateSent)).ToList();

        foreach (var entry in matched)
        {
            var provider = providerBySid[entry.ProviderMessageSid!];
            var notification = localBySid[entry.ProviderMessageSid!];
            notification.RecordProviderState(provider.Sid, provider.Status, provider.ErrorCode,
                provider.DateSent, _timeProvider.GetUtcNow());
        }
        await _db.SaveChangesAsync(cancellationToken);

        return new ReconciliationResponse(from, to, matched, providerOnly, applicationOnly);
    }

    private async Task<Order> FindOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        return order ?? throw new NotificationApiException((int)HttpStatusCode.NotFound,
            "Order not found.");
    }

    private async Task NotifyAllContactsAsync(Order order, NotificationKind kind, string body,
        DateTimeOffset? scheduledFor, CancellationToken cancellationToken)
    {
        var contacts = await _db.ContactNumbers
            .Where(x => x.BuyerId == order.BuyerId)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        foreach (var contact in contacts)
        {
            var notification = new OrderNotification(order.Id, order.BuyerId, contact.Id,
                kind, body, _timeProvider.GetUtcNow(), scheduledFor);
            _db.OrderNotifications.Add(notification);
            await _db.SaveChangesAsync(cancellationToken);
            await SendNotificationAsync(notification, contact.PhoneNumber, cancellationToken);
        }
    }

    private async Task SendNotificationAsync(OrderNotification notification, string destination,
        CancellationToken cancellationToken)
    {
        try
        {
            var message = await _twilio.SendMessageAsync(destination, notification.Body!,
                notification.ScheduledFor, cancellationToken);
            notification.RecordProviderState(message.Sid, message.Status, message.ErrorCode,
                message.DateSent, _timeProvider.GetUtcNow());
        }
        catch (TwilioRequestException ex)
        {
            notification.RecordSendFailure(ex.ProviderCode, _timeProvider.GetUtcNow());
        }
        catch (HttpRequestException)
        {
            notification.RecordSendFailure(null, _timeProvider.GetUtcNow());
        }
        catch (TaskCanceledException)
        {
            notification.RecordSendFailure(null, _timeProvider.GetUtcNow());
        }
        catch (Exception)
        {
            // A provider response or client failure must not roll back the business operation.
            notification.RecordSendFailure(null, _timeProvider.GetUtcNow());
        }

        await _db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task RefreshProviderStatesAsync(IEnumerable<OrderNotification> notifications,
        CancellationToken cancellationToken)
    {
        foreach (var notification in notifications.Where(x =>
                     !string.IsNullOrWhiteSpace(x.ProviderSid)))
        {
            try
            {
                var provider = await _twilio.FetchMessageAsync(notification.ProviderSid!,
                    cancellationToken);
                notification.RecordProviderState(provider.Sid, provider.Status, provider.ErrorCode,
                    provider.DateSent, _timeProvider.GetUtcNow());
            }
            catch (TwilioRequestException)
            {
                // Preserve the last known state; reads remain available during provider outages.
            }
            catch (HttpRequestException)
            {
                // Preserve the last known state; reads remain available during provider outages.
            }
        }
        await _db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<bool> CancelScheduledProviderMessageAsync(OrderNotification notification,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(notification.ProviderSid))
        {
            return true;
        }

        try
        {
            var current = await _twilio.FetchMessageAsync(notification.ProviderSid,
                cancellationToken);
            if (!string.Equals(current.Status, "scheduled", StringComparison.OrdinalIgnoreCase))
            {
                notification.RecordProviderState(current.Sid, current.Status, current.ErrorCode,
                    current.DateSent, _timeProvider.GetUtcNow());
                return true;
            }

            var canceled = await _twilio.CancelMessageAsync(notification.ProviderSid,
                cancellationToken);
            notification.RecordProviderState(canceled.Sid, canceled.Status, canceled.ErrorCode,
                canceled.DateSent, _timeProvider.GetUtcNow());
            notification.RecordCancellationSuccess(_timeProvider.GetUtcNow());
            return string.Equals(canceled.Status, "canceled", StringComparison.OrdinalIgnoreCase);
        }
        catch (TwilioRequestException ex)
        {
            notification.RecordCancellationFailure(ex.ProviderCode, _timeProvider.GetUtcNow());
            return false;
        }
        catch (HttpRequestException)
        {
            notification.RecordCancellationFailure(null, _timeProvider.GetUtcNow());
            return false;
        }
        catch (Exception)
        {
            notification.RecordCancellationFailure(null, _timeProvider.GetUtcNow());
            return false;
        }
    }

    private static bool IsPotentiallyScheduled(OrderNotification notification) =>
        notification.Kind == NotificationKind.DeliveryFollowUp &&
        !string.Equals(notification.ProviderStatus, "canceled", StringComparison.OrdinalIgnoreCase) &&
        notification.ScheduledFor.HasValue;

    private static bool IsUndelivered(string status) =>
        string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "undelivered", StringComparison.OrdinalIgnoreCase);

    private static NotificationDto Map(OrderNotification x) => new(
        x.Id, x.OrderId, x.Kind.ToString(), x.Body, x.ContentDisposedAt.HasValue,
        x.ProviderStatus, x.ProviderSid, x.ProviderErrorCode, x.CreatedAt, x.ScheduledFor,
        x.ProviderDateSent, x.LastCheckedAt, x.OriginalNotificationId,
        x.CancellationRequestedAt, x.CancellationErrorCode);

    private static ReconciliationEntry ProviderEntry(string source, ProviderMessage provider,
        OrderNotification? notification) => new(source, provider.Sid, notification?.Id,
        notification?.OrderId, provider.Status, provider.DateSent);
}
