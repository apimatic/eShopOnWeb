using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

public sealed class OrderNotificationApplicationService
{
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);
    private readonly CatalogContext _db;
    private readonly ITwilioMessagingClient _twilio;

    public OrderNotificationApplicationService(CatalogContext db, ITwilioMessagingClient twilio)
    {
        _db = db;
        _twilio = twilio;
    }

    public async Task<RegisterContactNumberResponse> RegisterContactAsync(
        string buyerId,
        string number,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(number) || number.Length > 64)
        {
            throw BadRequest("A phone number is required.");
        }

        PhoneValidationResult validation;
        try
        {
            validation = await _twilio.ValidatePhoneNumberAsync(number, cancellationToken);
        }
        catch (TwilioProviderException)
        {
            throw Unavailable("The phone number could not be validated right now.");
        }
        catch (HttpRequestException)
        {
            throw Unavailable("The phone number could not be validated right now.");
        }

        if (!validation.IsValid || string.IsNullOrWhiteSpace(validation.CanonicalNumber))
        {
            throw BadRequest("The messaging provider does not consider this a valid destination.");
        }

        var existing = await _db.ContactNumbers
            .SingleOrDefaultAsync(x => x.BuyerId == buyerId &&
                                       x.CanonicalNumber == validation.CanonicalNumber &&
                                       x.DeletedAt == null,
                cancellationToken);
        if (existing is not null)
        {
            return new RegisterContactNumberResponse(existing.Id, existing.CanonicalNumber);
        }

        var contact = new ContactNumber(buyerId, validation.CanonicalNumber);
        _db.ContactNumbers.Add(contact);
        await _db.SaveChangesAsync(cancellationToken);
        return new RegisterContactNumberResponse(contact.Id, contact.CanonicalNumber);
    }

    public async Task<IReadOnlyList<ContactNumberResponse>> GetContactsAsync(
        string buyerId,
        CancellationToken cancellationToken)
    {
        return await _db.ContactNumbers.AsNoTracking()
            .Where(x => x.BuyerId == buyerId && x.DeletedAt == null)
            .OrderBy(x => x.Id)
            .Select(x => new ContactNumberResponse(x.Id, x.CanonicalNumber, x.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task DeleteContactAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken)
    {
        var contact = await _db.ContactNumbers
            .SingleOrDefaultAsync(x => x.Id == contactNumberId && x.BuyerId == buyerId && x.DeletedAt == null,
                cancellationToken);
        if (contact is null)
        {
            throw NotFound("Contact number not found.");
        }

        var scheduled = await _db.OrderNotifications
            .Where(x => x.ContactNumberId == contact.Id &&
                        x.Kind == NotificationKind.DeliveryFollowUp &&
                        x.ProviderMessageSid != null &&
                        x.ProviderStatus == "scheduled")
            .ToListAsync(cancellationToken);

        await CancelScheduledMessagesAsync(scheduled, cancellationToken);
        contact.Delete();
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<PlaceOrderResponse> PlaceOrderAsync(
        string buyerId,
        PlaceOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Items.Count == 0 || request.Items.Any(x => x.CatalogItemId <= 0 || x.Quantity <= 0))
        {
            throw BadRequest("At least one catalog item with a positive quantity is required.");
        }

        var requestedItems = request.Items
            .GroupBy(x => x.CatalogItemId)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.Quantity));
        var catalogItems = await _db.CatalogItems
            .Where(x => requestedItems.Keys.Contains(x.Id))
            .ToListAsync(cancellationToken);
        if (catalogItems.Count != requestedItems.Count)
        {
            throw BadRequest("One or more catalog items do not exist.");
        }

        var orderItems = catalogItems.Select(item => new OrderItem(
            new CatalogItemOrdered(item.Id, item.Name, item.PictureUri),
            item.Price,
            requestedItems[item.Id])).ToList();
        var addressRequest = request.ShippingAddress ?? new ShippingAddressRequest();
        var address = new Address(
            RequiredAddressPart(addressRequest.Street),
            RequiredAddressPart(addressRequest.City),
            addressRequest.State ?? string.Empty,
            RequiredAddressPart(addressRequest.Country),
            RequiredAddressPart(addressRequest.ZipCode));

        var order = new Order(buyerId, address, orderItems);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);

        var contacts = await ActiveContactsAsync(buyerId, cancellationToken);
        foreach (var contact in contacts)
        {
            await SendAndRecordAsync(
                order,
                contact,
                NotificationKind.OrderPlaced,
                $"eShopOnWeb: order #{order.Id} was placed successfully.",
                null,
                null,
                null,
                cancellationToken);
        }

        return new PlaceOrderResponse(order.Id);
    }

    public async Task DispatchOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        if (order is null)
        {
            throw NotFound("Order not found.");
        }

        if (order.Status != OrderStatus.Placed)
        {
            throw Conflict("Only a placed order can be dispatched.");
        }

        order.Dispatch();
        await _db.SaveChangesAsync(cancellationToken);

        var contacts = await ActiveContactsAsync(order.BuyerId, cancellationToken);
        foreach (var contact in contacts)
        {
            await SendAndRecordAsync(
                order,
                contact,
                NotificationKind.OrderDispatched,
                $"eShopOnWeb: order #{order.Id} is on its way.",
                null,
                null,
                null,
                cancellationToken);

            var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
            await SendAndRecordAsync(
                order,
                contact,
                NotificationKind.DeliveryFollowUp,
                $"eShopOnWeb: how did delivery of order #{order.Id} go?",
                sendAt,
                null,
                null,
                cancellationToken);
        }
    }

    public async Task CancelOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        if (order is null)
        {
            throw NotFound("Order not found.");
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            return;
        }

        var followUps = await _db.OrderNotifications
            .Where(x => x.OrderId == order.Id &&
                        x.Kind == NotificationKind.DeliveryFollowUp &&
                        x.ProviderMessageSid != null &&
                        x.ProviderStatus == "scheduled")
            .ToListAsync(cancellationToken);

        // The provider cancellation is the safety boundary: do not declare the order
        // cancelled while a delivery survey is still scheduled to go out.
        await CancelScheduledMessagesAsync(followUps, cancellationToken);

        order.Cancel();
        await _db.SaveChangesAsync(cancellationToken);

        var contacts = await ActiveContactsAsync(order.BuyerId, cancellationToken);
        foreach (var contact in contacts)
        {
            await SendAndRecordAsync(
                order,
                contact,
                NotificationKind.OrderCancelled,
                $"eShopOnWeb: order #{order.Id} was cancelled.",
                null,
                null,
                null,
                cancellationToken);
        }
    }

    public async Task<IReadOnlyList<MyOrderResponse>> GetMyOrdersAsync(
        string buyerId,
        CancellationToken cancellationToken)
    {
        var orders = await _db.Orders.AsNoTracking()
            .Include(x => x.OrderItems)
            .Where(x => x.BuyerId == buyerId)
            .OrderByDescending(x => x.OrderDate)
            .ToListAsync(cancellationToken);
        var notifications = await _db.OrderNotifications
            .Where(x => x.BuyerId == buyerId)
            .ToListAsync(cancellationToken);
        await RefreshProviderStatesAsync(notifications, cancellationToken);

        return orders.Select(order =>
        {
            var orderNotifications = notifications.Where(x => x.OrderId == order.Id).ToList();
            var statuses = orderNotifications.GroupBy(x => x.ProviderStatus)
                .ToDictionary(x => x.Key, x => x.Count(), StringComparer.OrdinalIgnoreCase);
            return new MyOrderResponse(
                order.Id,
                order.OrderDate,
                order.Status.ToString(),
                order.Total(),
                orderNotifications.Count,
                statuses);
        }).ToList();
    }

    public async Task<IReadOnlyList<NotificationResponse>> GetOrderNotificationsAsync(
        string buyerId,
        int orderId,
        CancellationToken cancellationToken)
    {
        var ownsOrder = await _db.Orders.AnyAsync(x => x.Id == orderId && x.BuyerId == buyerId, cancellationToken);
        if (!ownsOrder)
        {
            throw NotFound("Order not found.");
        }

        var notifications = await _db.OrderNotifications
            .Where(x => x.OrderId == orderId && x.BuyerId == buyerId)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        await RefreshProviderStatesAsync(notifications, cancellationToken);
        return notifications.Select(Map).ToList();
    }

    public async Task<ResendNotificationResponse> ResendAsync(
        int sourceNotificationId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        idempotencyKey = idempotencyKey?.Trim() ?? string.Empty;
        if (idempotencyKey.Length is < 1 or > 128)
        {
            throw BadRequest("An idempotency key between 1 and 128 characters is required.");
        }

        var existing = await _db.OrderNotifications.SingleOrDefaultAsync(
            x => x.ResendOfNotificationId == sourceNotificationId && x.IdempotencyKey == idempotencyKey,
            cancellationToken);
        if (existing is not null)
        {
            return new ResendNotificationResponse(existing.Id);
        }

        var source = await _db.OrderNotifications.SingleOrDefaultAsync(x => x.Id == sourceNotificationId, cancellationToken);
        if (source is null)
        {
            throw NotFound("Notification not found.");
        }

        var order = await _db.Orders.SingleAsync(x => x.Id == source.OrderId, cancellationToken);
        var contact = await _db.ContactNumbers.SingleAsync(x => x.Id == source.ContactNumberId, cancellationToken);
        if (order.Status == OrderStatus.Cancelled || contact.DeletedAt is not null)
        {
            throw Conflict("This notification can no longer be sent.");
        }

        await RefreshProviderStatesAsync(new[] { source }, cancellationToken);
        if (!IsFailedOutcome(source.ProviderStatus))
        {
            throw Conflict("Only a notification that did not reach the shopper can be resent.");
        }

        if (source.Body is null)
        {
            throw Conflict("Disposed notification content cannot be resent.");
        }

        try
        {
            var resend = await SendAndRecordAsync(
                order,
                contact,
                NotificationKind.Resend,
                source.Body,
                null,
                source.Id,
                idempotencyKey,
                cancellationToken);
            return new ResendNotificationResponse(resend.Id);
        }
        catch (DbUpdateException)
        {
            // A concurrent request with the same key may have won the unique-index race.
            _db.ChangeTracker.Clear();
            existing = await _db.OrderNotifications.AsNoTracking().SingleOrDefaultAsync(
                x => x.ResendOfNotificationId == sourceNotificationId && x.IdempotencyKey == idempotencyKey,
                cancellationToken);
            if (existing is not null)
            {
                return new ResendNotificationResponse(existing.Id);
            }

            throw;
        }
    }

    public async Task DisposeContentAsync(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _db.OrderNotifications.SingleOrDefaultAsync(x => x.Id == notificationId, cancellationToken);
        if (notification is null)
        {
            throw NotFound("Notification not found.");
        }

        if (notification.ContentDisposedAt is not null)
        {
            return;
        }

        if (notification.ProviderMessageSid is not null)
        {
            try
            {
                await _twilio.RedactContentAsync(notification.ProviderMessageSid, cancellationToken);
            }
            catch (TwilioProviderException)
            {
                throw Unavailable("The provider could not dispose of the message content.");
            }
            catch (HttpRequestException)
            {
                throw Unavailable("The provider could not dispose of the message content.");
            }
        }

        notification.DisposeContent();
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<ReconciliationResponse> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (from > to)
        {
            throw BadRequest("The from date must not be later than the to date.");
        }

        IReadOnlyList<ProviderMessage> allProviderMessages;
        try
        {
            allProviderMessages = await _twilio.ListFromNumberAsync(cancellationToken);
        }
        catch (TwilioProviderException)
        {
            throw Unavailable("The provider reconciliation data is unavailable.");
        }
        catch (HttpRequestException)
        {
            throw Unavailable("The provider reconciliation data is unavailable.");
        }

        var providerMessages = allProviderMessages
            .Where(x => x.DateCreated >= from && x.DateCreated <= to)
            .ToDictionary(x => x.Sid, StringComparer.Ordinal);
        var localNotifications = await _db.OrderNotifications
            .Where(x => x.CreatedAt >= from && x.CreatedAt <= to)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);

        var entries = new List<ReconciliationEntryResponse>();
        foreach (var local in localNotifications)
        {
            if (local.ProviderMessageSid is not null && providerMessages.Remove(local.ProviderMessageSid, out var provider))
            {
                local.RecordProviderState(provider.Sid, provider.Status, provider.ErrorCode);
                entries.Add(new ReconciliationEntryResponse(
                    "matched", local.Id, local.OrderId, provider.Sid, provider.Status, provider.ErrorCode,
                    local.CreatedAt, provider.DateCreated, provider.DateSent));
            }
            else
            {
                entries.Add(new ReconciliationEntryResponse(
                    "applicationOnly", local.Id, local.OrderId, local.ProviderMessageSid, local.ProviderStatus,
                    local.ProviderErrorCode, local.CreatedAt, null, null));
            }
        }

        entries.AddRange(providerMessages.Values.Select(provider => new ReconciliationEntryResponse(
            "providerOnly", null, null, provider.Sid, provider.Status, provider.ErrorCode,
            null, provider.DateCreated, provider.DateSent)));
        await _db.SaveChangesAsync(cancellationToken);

        return new ReconciliationResponse(
            from,
            to,
            entries.Count(x => x.MatchStatus == "matched"),
            entries.Count(x => x.MatchStatus == "providerOnly"),
            entries.Count(x => x.MatchStatus == "applicationOnly"),
            entries.OrderBy(x => x.ApplicationCreatedAt ?? x.ProviderCreatedAt).ToList());
    }

    private async Task<List<ContactNumber>> ActiveContactsAsync(string buyerId, CancellationToken cancellationToken)
    {
        return await _db.ContactNumbers
            .Where(x => x.BuyerId == buyerId && x.DeletedAt == null)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
    }

    private async Task<OrderNotification> SendAndRecordAsync(
        Order order,
        ContactNumber contact,
        NotificationKind kind,
        string body,
        DateTimeOffset? scheduledFor,
        int? resendOfNotificationId,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var notification = new OrderNotification(
            order.Id,
            contact.Id,
            order.BuyerId,
            kind,
            body,
            scheduledFor,
            resendOfNotificationId,
            idempotencyKey);
        _db.OrderNotifications.Add(notification);
        await _db.SaveChangesAsync(cancellationToken);

        try
        {
            var providerMessage = await _twilio.SendAsync(contact.CanonicalNumber, body, scheduledFor, cancellationToken);
            notification.RecordProviderState(providerMessage.Sid, providerMessage.Status, providerMessage.ErrorCode);
        }
        catch (TwilioProviderException ex)
        {
            notification.RecordFailure(ex.ProviderCode);
        }
        catch (HttpRequestException)
        {
            notification.RecordFailure(null);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            notification.RecordFailure(null);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return notification;
    }

    private async Task CancelScheduledMessagesAsync(
        IReadOnlyCollection<OrderNotification> scheduled,
        CancellationToken cancellationToken)
    {
        foreach (var notification in scheduled)
        {
            try
            {
                var provider = await _twilio.GetAsync(notification.ProviderMessageSid!, cancellationToken);
                notification.RecordProviderState(provider.Sid, provider.Status, provider.ErrorCode);
                if (provider.Status == "scheduled")
                {
                    provider = await _twilio.CancelAsync(provider.Sid, cancellationToken);
                    notification.RecordProviderState(provider.Sid, provider.Status, provider.ErrorCode);
                }
                else if (provider.Status != "canceled")
                {
                    throw Conflict("A delivery follow-up is no longer cancelable.");
                }
            }
            catch (NotificationApiException)
            {
                throw;
            }
            catch (TwilioProviderException)
            {
                throw Unavailable("A scheduled delivery follow-up could not be cancelled safely.");
            }
            catch (HttpRequestException)
            {
                throw Unavailable("A scheduled delivery follow-up could not be cancelled safely.");
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task RefreshProviderStatesAsync(
        IEnumerable<OrderNotification> notifications,
        CancellationToken cancellationToken)
    {
        var changed = false;
        foreach (var notification in notifications.Where(x => x.ProviderMessageSid is not null))
        {
            try
            {
                var provider = await _twilio.GetAsync(notification.ProviderMessageSid!, cancellationToken);
                notification.RecordProviderState(provider.Sid, provider.Status, provider.ErrorCode);
                changed = true;
            }
            catch (TwilioProviderException)
            {
                // A read still returns the last durable state when Twilio is temporarily unavailable.
            }
            catch (HttpRequestException)
            {
                // A read still returns the last durable state when Twilio is temporarily unavailable.
            }
        }

        if (changed)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    private static NotificationResponse Map(OrderNotification notification) => new(
        notification.Id,
        notification.OrderId,
        notification.Kind.ToString(),
        notification.Body,
        notification.ProviderMessageSid,
        notification.ProviderStatus,
        notification.ProviderErrorCode,
        notification.CreatedAt,
        notification.UpdatedAt,
        notification.ScheduledFor,
        notification.ContentDisposedAt,
        notification.ResendOfNotificationId);

    private static bool IsFailedOutcome(string status) => status is "failed" or "undelivered";
    private static string RequiredAddressPart(string? value) => string.IsNullOrWhiteSpace(value) ? "Not supplied" : value;
    private static NotificationApiException BadRequest(string message) => new((int)HttpStatusCode.BadRequest, message);
    private static NotificationApiException NotFound(string message) => new((int)HttpStatusCode.NotFound, message);
    private static NotificationApiException Conflict(string message) => new((int)HttpStatusCode.Conflict, message);
    private static NotificationApiException Unavailable(string message) => new((int)HttpStatusCode.ServiceUnavailable, message);
}
