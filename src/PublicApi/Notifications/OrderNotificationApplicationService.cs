using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

public sealed class OrderNotificationApplicationService(
    CatalogContext db,
    ITwilioMessagingService twilio,
    IUriComposer uriComposer,
    IOptions<TwilioSettings> settings,
    NotificationIdempotencyLock idempotencyLock,
    ILogger<OrderNotificationApplicationService> logger)
{
    private const int MaximumActiveContactNumbers = 5;
    private const int MaximumItemQuantity = 100;
    private const int MaximumReconciliationPages = 10_000;
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);
    private readonly TwilioSettings _settings = settings.Value;

    public async Task<Guid> RegisterContactNumberAsync(string shopperId, string input, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new ApiRequestException(StatusCodes.Status400BadRequest, "A mobile number is required.");

        string? canonical;
        try
        {
            canonical = await twilio.ValidateAndCanonicalizeAsync(input, cancellationToken);
        }
        catch (TwilioProviderException ex) when (IsCallerCorrectable(ex.StatusCode))
        {
            throw new ApiRequestException(StatusCodes.Status400BadRequest, "The mobile number is not a usable SMS destination.");
        }
        catch (TwilioProviderException)
        {
            throw new ApiRequestException(StatusCodes.Status503ServiceUnavailable, "Mobile-number validation is temporarily unavailable.");
        }

        if (string.IsNullOrWhiteSpace(canonical))
            throw new ApiRequestException(StatusCodes.Status400BadRequest, "The mobile number is not a usable SMS destination.");

        var activeCount = await db.ContactNumbers.CountAsync(
            x => x.ShopperId == shopperId && x.DeletedAt == null,
            cancellationToken);
        if (activeCount >= MaximumActiveContactNumbers)
            throw new ApiRequestException(StatusCodes.Status409Conflict, "The maximum number of registered mobile numbers has been reached.");

        var duplicate = await db.ContactNumbers.AnyAsync(
            x => x.ShopperId == shopperId && x.CanonicalNumber == canonical && x.DeletedAt == null,
            cancellationToken);
        if (duplicate)
            throw new ApiRequestException(StatusCodes.Status409Conflict, "That mobile number is already registered.");

        var contact = new ContactNumber(shopperId, canonical, DateTimeOffset.UtcNow);
        db.ContactNumbers.Add(contact);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw new ApiRequestException(StatusCodes.Status409Conflict, "That mobile number is already registered.");
        }

        return contact.Id;
    }

    public async Task<IReadOnlyList<ContactNumberResponse>> GetContactNumbersAsync(string shopperId, CancellationToken cancellationToken) =>
        await db.ContactNumbers.AsNoTracking()
            .Where(x => x.ShopperId == shopperId && x.DeletedAt == null)
            .OrderBy(x => x.CreatedAt)
            .Select(x => new ContactNumberResponse(x.Id, x.CanonicalNumber, x.CreatedAt))
            .ToListAsync(cancellationToken);

    public async Task<bool> DeleteContactNumberAsync(string shopperId, Guid contactNumberId, CancellationToken cancellationToken)
    {
        var contact = await db.ContactNumbers.SingleOrDefaultAsync(
            x => x.Id == contactNumberId && x.ShopperId == shopperId,
            cancellationToken);
        if (contact is null) return false;

        contact.Delete(DateTimeOffset.UtcNow);
        var scheduled = await db.OrderNotifications
            .Where(x => x.ContactNumberId == contact.Id && x.Kind == NotificationKind.DeliveryFollowUp &&
                x.ProviderSid != null && x.CancellationState != ProviderActionState.Confirmed &&
                x.ProviderStatus != "delivered" && x.ProviderStatus != "undelivered" &&
                x.ProviderStatus != "failed" && x.ProviderStatus != "canceled")
            .ToListAsync(cancellationToken);
        foreach (var notification in scheduled) notification.RequestCancellation();
        await db.SaveChangesAsync(cancellationToken);

        foreach (var notification in scheduled) await TryCancelAsync(notification, cancellationToken);
        await db.SaveChangesAsync(CancellationToken.None);
        if (scheduled.Any(x => x.CancellationState != ProviderActionState.Confirmed))
            throw new ApiRequestException(
                StatusCodes.Status503ServiceUnavailable,
                "The number is removed locally; cancellation of a queued provider message is still pending.");
        return true;
    }

    public async Task<int> PlaceOrderAsync(string shopperId, PlaceOrderRequest request, CancellationToken cancellationToken)
    {
        if (request.Items is null || request.Items.Count == 0)
            throw new ApiRequestException(StatusCodes.Status400BadRequest, "At least one order item is required.");
        if (request.Items.Count > 100 || request.Items.Any(x => x.CatalogItemId <= 0 || x.Quantity <= 0 || x.Quantity > MaximumItemQuantity))
            throw new ApiRequestException(StatusCodes.Status400BadRequest, "Every catalog item and quantity must be valid.");

        var requestedItems = request.Items
            .GroupBy(x => x.CatalogItemId)
            .Select(group => new { CatalogItemId = group.Key, Quantity = group.Sum(x => x.Quantity) })
            .ToList();
        if (requestedItems.Any(x => x.Quantity > MaximumItemQuantity))
            throw new ApiRequestException(StatusCodes.Status400BadRequest, "An item's total quantity cannot exceed 100.");

        var ids = requestedItems.Select(x => x.CatalogItemId).ToArray();
        var catalogItems = await db.CatalogItems
            .Where(x => ids.Contains(x.Id))
            .ToListAsync(cancellationToken);
        if (catalogItems.Count != ids.Length)
            throw new ApiRequestException(StatusCodes.Status400BadRequest, "One or more catalog items do not exist.");

        var itemById = catalogItems.ToDictionary(x => x.Id);
        var orderItems = requestedItems.Select(item =>
        {
            var catalogItem = itemById[item.CatalogItemId];
            var ordered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(ordered, catalogItem.Price, item.Quantity);
        }).ToList();

        var address = BuildAddress(request.ShippingAddress);
        var order = new Order(shopperId, address, orderItems);
        db.Orders.Add(order);
        await db.SaveChangesAsync(cancellationToken);

        await NotifyActiveContactsAsync(
            order,
            NotificationKind.OrderPlaced,
            $"Order {order.Id} has been placed.",
            scheduledFor: null,
            cancellationToken);

        return order.Id;
    }

    public async Task<OrderLifecycleStatus> DispatchOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken)
            ?? throw new ApiRequestException(StatusCodes.Status404NotFound, "Order not found.");

        bool changed;
        try
        {
            changed = order.MarkDispatched(DateTimeOffset.UtcNow);
        }
        catch (InvalidOperationException)
        {
            throw new ApiRequestException(StatusCodes.Status409Conflict, "A cancelled order cannot be dispatched.");
        }

        if (!changed) return order.Status;
        await SaveOrderTransitionAsync(cancellationToken);

        await NotifyActiveContactsAsync(
            order,
            NotificationKind.OrderDispatched,
            $"Order {order.Id} has been dispatched and is on its way.",
            scheduledFor: null,
            cancellationToken);

        await NotifyActiveContactsAsync(
            order,
            NotificationKind.DeliveryFollowUp,
            $"How did delivery of order {order.Id} go?",
            scheduledFor: DateTimeOffset.UtcNow.Add(FollowUpDelay),
            cancellationToken);

        return order.Status;
    }

    public async Task<OrderLifecycleStatus> CancelOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken)
            ?? throw new ApiRequestException(StatusCodes.Status404NotFound, "Order not found.");

        var changed = order.MarkCancelled(DateTimeOffset.UtcNow);
        if (changed) await SaveOrderTransitionAsync(cancellationToken);

        await CancelFollowUpsAsync(orderId, cancellationToken);

        if (changed)
        {
            await NotifyActiveContactsAsync(
                order,
                NotificationKind.OrderCancelled,
                $"Order {order.Id} has been cancelled.",
                scheduledFor: null,
                cancellationToken);
        }

        return order.Status;
    }

    public async Task<IReadOnlyList<MyOrderResponse>> GetMyOrdersAsync(string shopperId, CancellationToken cancellationToken)
    {
        var orders = await db.Orders
            .Include(x => x.OrderItems)
            .Where(x => x.BuyerId == shopperId)
            .OrderByDescending(x => x.OrderDate)
            .ToListAsync(cancellationToken);

        var orderIds = orders.Select(x => x.Id).ToArray();
        var notifications = await db.OrderNotifications
            .Where(x => orderIds.Contains(x.OrderId))
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
        await RefreshNotificationsAsync(notifications, cancellationToken);

        return orders.Select(order => new MyOrderResponse(
            order.Id,
            order.Status.ToString(),
            order.OrderDate,
            order.DispatchedAt,
            order.CancelledAt,
            order.Total(),
            notifications.Where(x => x.OrderId == order.Id).Select(MapNotification).ToList()))
            .ToList();
    }

    public async Task<IReadOnlyList<NotificationResponse>?> GetOrderNotificationsAsync(
        string shopperId,
        int orderId,
        CancellationToken cancellationToken)
    {
        var owned = await db.Orders.AnyAsync(x => x.Id == orderId && x.BuyerId == shopperId, cancellationToken);
        if (!owned) return null;

        var notifications = await db.OrderNotifications
            .Where(x => x.OrderId == orderId)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
        await RefreshNotificationsAsync(notifications, cancellationToken);
        return notifications.Select(MapNotification).ToList();
    }

    public async Task<Guid> ResendAsync(Guid originalNotificationId, string idempotencyKey, CancellationToken cancellationToken)
    {
        idempotencyKey = idempotencyKey?.Trim() ?? string.Empty;
        if (idempotencyKey.Length is < 1 or > 128)
            throw new ApiRequestException(StatusCodes.Status400BadRequest, "A valid idempotency key is required.");

        OrderNotification? resend = null;
        using (await idempotencyLock.AcquireAsync(originalNotificationId, idempotencyKey, cancellationToken))
        {
            var existing = await db.OrderNotifications.SingleOrDefaultAsync(
                x => x.ResendOfNotificationId == originalNotificationId && x.IdempotencyKey == idempotencyKey,
                cancellationToken);
            if (existing is not null) return existing.Id;

            var original = await db.OrderNotifications.SingleOrDefaultAsync(x => x.Id == originalNotificationId, cancellationToken)
                ?? throw new ApiRequestException(StatusCodes.Status404NotFound, "Notification not found.");
            var order = await db.Orders.AsNoTracking().SingleAsync(x => x.Id == original.OrderId, cancellationToken);

            if (original.Kind == NotificationKind.DeliveryFollowUp && order.Status == OrderLifecycleStatus.Cancelled)
                throw new ApiRequestException(StatusCodes.Status409Conflict, "A cancelled delivery follow-up cannot be resent.");

            if (original.Content is null)
                throw new ApiRequestException(StatusCodes.Status409Conflict, "Disposed message content cannot be resent.");

            if (!CanResend(original))
                throw new ApiRequestException(StatusCodes.Status409Conflict, "Only a message that did not reach the shopper can be resent.");

            var contact = await db.ContactNumbers.SingleOrDefaultAsync(
                x => x.Id == original.ContactNumberId && x.DeletedAt == null,
                cancellationToken);
            if (contact is null)
                throw new ApiRequestException(StatusCodes.Status409Conflict, "The original destination is no longer registered.");

            resend = new OrderNotification(
                original.OrderId,
                contact.Id,
                NotificationKind.Resend,
                original.Content,
                DateTimeOffset.UtcNow,
                resendOfNotificationId: original.Id,
                idempotencyKey: idempotencyKey);
            db.OrderNotifications.Add(resend);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                db.Entry(resend).State = EntityState.Detached;
                var raced = await db.OrderNotifications.SingleAsync(
                    x => x.ResendOfNotificationId == originalNotificationId && x.IdempotencyKey == idempotencyKey,
                    cancellationToken);
                return raced.Id;
            }
        }

        var destination = await db.ContactNumbers.AsNoTracking().SingleAsync(x => x.Id == resend.ContactNumberId, cancellationToken);
        await SubmitAsync(resend, destination.CanonicalNumber, scheduled: false, cancellationToken);
        return resend.Id;
    }

    public async Task DisposeContentAsync(Guid notificationId, CancellationToken cancellationToken)
    {
        var notification = await db.OrderNotifications.SingleOrDefaultAsync(x => x.Id == notificationId, cancellationToken)
            ?? throw new ApiRequestException(StatusCodes.Status404NotFound, "Notification not found.");

        if (notification.RedactionState == ProviderActionState.Confirmed) return;
        notification.RequestRedaction();
        await db.SaveChangesAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(notification.ProviderSid))
        {
            notification.ConfirmRedaction(DateTimeOffset.UtcNow);
            await db.SaveChangesAsync(CancellationToken.None);
            return;
        }

        try
        {
            var snapshot = await twilio.RedactAsync(notification.ProviderSid, cancellationToken);
            ApplySnapshot(notification, snapshot);
            if (!string.IsNullOrEmpty(snapshot.Body))
            {
                snapshot = await twilio.FetchAsync(notification.ProviderSid, cancellationToken);
                ApplySnapshot(notification, snapshot);
            }

            if (!string.IsNullOrEmpty(snapshot.Body))
                throw new TwilioProviderException("The provider did not confirm message-content disposal.");

            notification.ConfirmRedaction(DateTimeOffset.UtcNow);
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (TwilioProviderException)
        {
            notification.MarkRefreshFailed(DateTimeOffset.UtcNow);
            await db.SaveChangesAsync(CancellationToken.None);
            throw new ApiRequestException(StatusCodes.Status503ServiceUnavailable, "Message content is hidden locally; provider disposal is pending.");
        }
    }

    public async Task<ReconciliationResponse> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var providerMessages = new List<ProviderMessage>();
        var seenTokens = new HashSet<string>(StringComparer.Ordinal);
        string? pageToken = null;

        for (var page = 0; page < MaximumReconciliationPages; page++)
        {
            ProviderMessagePage response;
            try
            {
                response = await twilio.ListAsync(from.AddTicks(-1), to, pageToken, cancellationToken);
            }
            catch (TwilioProviderException)
            {
                throw new ApiRequestException(StatusCodes.Status503ServiceUnavailable, "Notification reconciliation is temporarily unavailable.");
            }

            providerMessages.AddRange(response.Messages.Where(message =>
            {
                var date = ProviderDate(message);
                return date is null || (date >= from && date < to);
            }));

            if (string.IsNullOrWhiteSpace(response.NextPageToken)) break;
            if (!seenTokens.Add(response.NextPageToken))
                throw new ApiRequestException(StatusCodes.Status502BadGateway, "The provider returned a non-progressing reconciliation page.");
            pageToken = response.NextPageToken;

            if (page == MaximumReconciliationPages - 1)
                throw new ApiRequestException(StatusCodes.Status502BadGateway, "The provider reconciliation exceeded its safety limit.");
        }

        var providerSids = providerMessages
            .Where(x => !string.IsNullOrWhiteSpace(x.Sid))
            .Select(x => x.Sid!)
            .Distinct()
            .ToArray();
        var local = await db.OrderNotifications.AsNoTracking()
            .Where(x => (x.CreatedAt >= from && x.CreatedAt < to) ||
                (x.ProviderSid != null && providerSids.Contains(x.ProviderSid)))
            .ToListAsync(cancellationToken);
        var localBySid = local.Where(x => !string.IsNullOrWhiteSpace(x.ProviderSid))
            .ToDictionary(x => x.ProviderSid!, StringComparer.Ordinal);
        var matchedLocalIds = new HashSet<Guid>();
        var entries = new List<ReconciliationEntryResponse>();

        foreach (var provider in providerMessages)
        {
            if (!string.IsNullOrWhiteSpace(provider.Sid) && localBySid.TryGetValue(provider.Sid, out var notification))
            {
                matchedLocalIds.Add(notification.Id);
                entries.Add(new ReconciliationEntryResponse(
                    "matched", notification.Id, provider.Sid, notification.ProviderStatus, provider.Status,
                    notification.CreatedAt, ProviderDate(provider)));
            }
            else
            {
                entries.Add(new ReconciliationEntryResponse(
                    "provider-only", null, provider.Sid, null, provider.Status, null, ProviderDate(provider)));
            }
        }

        entries.AddRange(local.Where(x => !matchedLocalIds.Contains(x.Id)).Select(notification =>
            new ReconciliationEntryResponse(
                "application-only", notification.Id, notification.ProviderSid, notification.ProviderStatus, null,
                notification.CreatedAt, null)));

        return new ReconciliationResponse(from, to, entries
            .OrderBy(x => x.ProviderDate ?? x.ApplicationCreatedAt)
            .ToList());
    }

    public async Task RetryPendingProviderActionsAsync(CancellationToken cancellationToken)
    {
        var pending = await db.OrderNotifications
            .Where(x => x.CancellationState == ProviderActionState.Pending || x.RedactionState == ProviderActionState.Pending)
            .ToListAsync(cancellationToken);
        await RetryPendingActionsAsync(pending, cancellationToken);
    }

    private async Task NotifyActiveContactsAsync(
        Order order,
        NotificationKind kind,
        string body,
        DateTimeOffset? scheduledFor,
        CancellationToken cancellationToken)
    {
        var contacts = await db.ContactNumbers.AsNoTracking()
            .Where(x => x.ShopperId == order.BuyerId && x.DeletedAt == null)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        foreach (var contact in contacts)
        {
            var notification = new OrderNotification(
                order.Id,
                contact.Id,
                kind,
                body,
                DateTimeOffset.UtcNow,
                scheduledFor);
            db.OrderNotifications.Add(notification);
            await db.SaveChangesAsync(cancellationToken);
            await SubmitAsync(notification, contact.CanonicalNumber, scheduledFor is not null, cancellationToken);
        }
    }

    private async Task SubmitAsync(
        OrderNotification notification,
        string canonicalDestination,
        bool scheduled,
        CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = scheduled
                ? await twilio.ScheduleAsync(canonicalDestination, notification.Content!, notification.ScheduledFor!.Value, cancellationToken)
                : await twilio.SendAsync(canonicalDestination, notification.Content!, cancellationToken);
            ApplySnapshot(notification, snapshot);

            if (string.IsNullOrWhiteSpace(snapshot.Sid))
                throw new TwilioProviderException("The provider accepted the request without returning a message identifier.", ambiguous: true);

            if (scheduled && !string.IsNullOrWhiteSpace(snapshot.From) &&
                !string.Equals(snapshot.From, _settings.FromNumber, StringComparison.Ordinal))
            {
                notification.RecordConfigurationFailure("The configured messaging service selected an unexpected sender.", DateTimeOffset.UtcNow);
                notification.RequestCancellation();
                await db.SaveChangesAsync(CancellationToken.None);
                await TryCancelAsync(notification, cancellationToken);
                notification.RecordConfigurationFailure("The configured messaging service selected an unexpected sender.", DateTimeOffset.UtcNow);
            }
        }
        catch (TwilioProviderException ex)
        {
            notification.RecordFailure(((int?)ex.StatusCode)?.ToString(), ex.Message, ex.IsAmbiguous, DateTimeOffset.UtcNow);
            logger.LogWarning("Provider submission failed for notification {NotificationId}; outcome {Outcome}.",
                notification.Id, ex.IsAmbiguous ? "ambiguous" : "rejected");
        }

        await db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task CancelFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var followUps = await db.OrderNotifications
            .Where(x => x.OrderId == orderId && x.Kind == NotificationKind.DeliveryFollowUp &&
                x.ProviderSid != null && x.CancellationState != ProviderActionState.Confirmed)
            .ToListAsync(cancellationToken);

        foreach (var followUp in followUps) followUp.RequestCancellation();
        await db.SaveChangesAsync(CancellationToken.None);
        foreach (var followUp in followUps) await TryCancelAsync(followUp, cancellationToken);
        await db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task TryCancelAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(notification.ProviderSid)) return;
        try
        {
            var snapshot = await twilio.CancelAsync(notification.ProviderSid, cancellationToken);
            ApplySnapshot(notification, snapshot);
            if (string.Equals(snapshot.Status, "canceled", StringComparison.OrdinalIgnoreCase))
                notification.ConfirmCancellation(snapshot.Status, DateTimeOffset.UtcNow);
            else
                notification.MarkRefreshFailed(DateTimeOffset.UtcNow);
        }
        catch (TwilioProviderException)
        {
            notification.MarkRefreshFailed(DateTimeOffset.UtcNow);
            logger.LogWarning("Provider cancellation remains pending for notification {NotificationId}.", notification.Id);
        }
    }

    private async Task RefreshNotificationsAsync(List<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        await RetryPendingActionsAsync(notifications, cancellationToken);
        foreach (var notification in notifications.Where(NeedsRefresh))
        {
            try
            {
                var snapshot = await twilio.FetchAsync(notification.ProviderSid!, cancellationToken);
                ApplySnapshot(notification, snapshot);
            }
            catch (TwilioProviderException)
            {
                notification.MarkRefreshFailed(DateTimeOffset.UtcNow);
            }
        }

        await db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task RetryPendingActionsAsync(List<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications.Where(x =>
            x.CancellationState == ProviderActionState.Pending && !string.IsNullOrWhiteSpace(x.ProviderSid)))
            await TryCancelAsync(notification, cancellationToken);

        foreach (var notification in notifications.Where(x =>
            x.RedactionState == ProviderActionState.Pending && !string.IsNullOrWhiteSpace(x.ProviderSid)))
        {
            try
            {
                var snapshot = await twilio.RedactAsync(notification.ProviderSid!, cancellationToken);
                ApplySnapshot(notification, snapshot);
                if (string.IsNullOrEmpty(snapshot.Body)) notification.ConfirmRedaction(DateTimeOffset.UtcNow);
            }
            catch (TwilioProviderException)
            {
                notification.MarkRefreshFailed(DateTimeOffset.UtcNow);
            }
        }
    }

    private async Task SaveOrderTransitionAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ApiRequestException(StatusCodes.Status409Conflict, "The order was changed by another request.");
        }
    }

    private static Address BuildAddress(ShippingAddressRequest? address)
    {
        if (address is null) return new Address("Not supplied", "Not supplied", string.Empty, "Not supplied", "Not supplied");
        if (string.IsNullOrWhiteSpace(address.Street) || string.IsNullOrWhiteSpace(address.City) ||
            string.IsNullOrWhiteSpace(address.Country) || string.IsNullOrWhiteSpace(address.ZipCode))
            throw new ApiRequestException(StatusCodes.Status400BadRequest, "The shipping address is incomplete.");
        return new Address(address.Street, address.City, address.State ?? string.Empty, address.Country, address.ZipCode);
    }

    private static void ApplySnapshot(OrderNotification notification, ProviderMessage message) =>
        notification.RecordProviderState(
            message.Sid,
            message.From,
            message.Status,
            message.ErrorCode,
            message.ErrorMessage,
            message.DateCreated,
            message.DateSent,
            message.DateUpdated,
            DateTimeOffset.UtcNow);

    private static bool NeedsRefresh(OrderNotification notification) =>
        !string.IsNullOrWhiteSpace(notification.ProviderSid) &&
        !IsTerminal(notification.ProviderStatus);

    private static bool IsTerminal(string? status) => status?.ToLowerInvariant() is
        "delivered" or "undelivered" or "failed" or "canceled" or "received" or "read" or "partially_delivered";

    private static bool CanResend(OrderNotification notification) =>
        notification.SubmissionStatus is NotificationSubmissionStatus.Rejected or NotificationSubmissionStatus.Ambiguous ||
        notification.ProviderStatus?.ToLowerInvariant() is "undelivered" or "failed";

    private static bool IsCallerCorrectable(HttpStatusCode? statusCode) => statusCode is >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError &&
        statusCode is not HttpStatusCode.Unauthorized and not HttpStatusCode.Forbidden and not HttpStatusCode.TooManyRequests;

    private static DateTimeOffset? ProviderDate(ProviderMessage message) =>
        message.DateSent ?? message.DateCreated ?? message.DateUpdated;

    private static NotificationResponse MapNotification(OrderNotification notification) => new(
        notification.Id,
        notification.Kind.ToString(),
        notification.SubmissionStatus.ToString(),
        notification.ProviderSid,
        notification.ProviderStatus,
        notification.ProviderErrorCode,
        notification.ProviderErrorMessage is null ? null :
            notification.SubmissionStatus == NotificationSubmissionStatus.Accepted
                ? "The provider reported a delivery issue."
                : notification.ProviderErrorMessage,
        notification.CreatedAt,
        notification.ScheduledFor,
        notification.ProviderSentAt,
        notification.CancellationState.ToString(),
        notification.RedactionState.ToString(),
        notification.Content,
        notification.Content is not null,
        notification.LastRefreshSucceeded);
}
