using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

public sealed class OrderNotificationService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ResendLocks = new(StringComparer.Ordinal);
    private static readonly HashSet<string> FailedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "failed", "undelivered", "partially_delivered"
    };

    private readonly CatalogContext _db;
    private readonly ITwilioMessagingGateway _provider;
    private readonly TwilioSettings _settings;

    public OrderNotificationService(
        CatalogContext db,
        ITwilioMessagingGateway provider,
        IOptions<TwilioSettings> settings)
    {
        _db = db;
        _provider = provider;
        _settings = settings.Value;
    }

    public async Task<RegisterContactNumberResponse> RegisterContactNumberAsync(
        string userId,
        RegisterContactNumberRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.MobileNumber))
        {
            throw new NotificationApiException(400, "A mobile number is required.");
        }

        ProviderPhoneValidation validation;
        try
        {
            validation = await _provider.ValidatePhoneNumberAsync(request.MobileNumber, cancellationToken);
        }
        catch (MessagingProviderException ex) when (IsCallerError(ex.StatusCode))
        {
            throw new NotificationApiException(400, "The mobile number is not a usable destination.");
        }
        catch (MessagingProviderException)
        {
            throw new NotificationApiException(502, "The mobile number could not be validated right now.");
        }

        if (!validation.IsValid || string.IsNullOrWhiteSpace(validation.CanonicalNumber))
        {
            throw new NotificationApiException(400, "The mobile number is not a usable destination.");
        }

        var existing = await _db.ContactNumbers
            .SingleOrDefaultAsync(
                x => x.UserId == userId && x.CanonicalNumber == validation.CanonicalNumber && x.DeletedAt == null,
                cancellationToken);
        if (existing is not null)
        {
            return new RegisterContactNumberResponse(existing.Id, existing.CanonicalNumber);
        }

        var contact = new ContactNumber(userId, validation.CanonicalNumber);
        _db.ContactNumbers.Add(contact);
        await _db.SaveChangesAsync(cancellationToken);
        return new RegisterContactNumberResponse(contact.Id, contact.CanonicalNumber);
    }

    public async Task<IReadOnlyList<ContactNumberDto>> GetContactNumbersAsync(string userId, CancellationToken cancellationToken) =>
        await _db.ContactNumbers
            .AsNoTracking()
            .Where(x => x.UserId == userId && x.DeletedAt == null)
            .OrderBy(x => x.Id)
            .Select(x => new ContactNumberDto(x.Id, x.CanonicalNumber, x.CreatedAt))
            .ToListAsync(cancellationToken);

    public async Task DeleteContactNumberAsync(string userId, int contactNumberId, CancellationToken cancellationToken)
    {
        var contact = await _db.ContactNumbers.SingleOrDefaultAsync(
            x => x.Id == contactNumberId && x.UserId == userId && x.DeletedAt == null,
            cancellationToken);
        if (contact is null)
        {
            throw new NotificationApiException(404, "Contact number not found.");
        }

        var scheduled = await _db.OrderNotifications
            .Where(x => x.ContactNumberId == contact.Id
                        && x.ScheduledFor != null
                        && x.ProviderSid != null
                        && x.ProviderStatus != "canceled"
                        && x.ProviderDateSent == null)
            .ToListAsync(cancellationToken);

        foreach (var notification in scheduled)
        {
            if (!await TryCancelScheduledAsync(notification, cancellationToken))
            {
                await _db.SaveChangesAsync(cancellationToken);
                throw new NotificationApiException(502, "The contact number could not be removed safely because a queued message could not be cancelled.");
            }
        }

        contact.Delete();
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<PlaceOrderResponse> PlaceOrderAsync(
        string userId,
        PlaceOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Items is null || request.Items.Count == 0 || request.Items.Any(x => x.CatalogItemId <= 0 || x.Quantity <= 0))
        {
            throw new NotificationApiException(400, "At least one catalog item with a positive quantity is required.");
        }

        if (request.ShippingAddress is null
            || string.IsNullOrWhiteSpace(request.ShippingAddress.Street)
            || string.IsNullOrWhiteSpace(request.ShippingAddress.City)
            || string.IsNullOrWhiteSpace(request.ShippingAddress.Country)
            || string.IsNullOrWhiteSpace(request.ShippingAddress.ZipCode))
        {
            throw new NotificationApiException(400, "A complete shipping address is required.");
        }

        var requestedLines = request.Items
            .GroupBy(x => x.CatalogItemId)
            .Select(x => new { CatalogItemId = x.Key, Quantity = x.Sum(y => y.Quantity) })
            .ToList();
        var ids = requestedLines.Select(x => x.CatalogItemId).ToArray();
        var catalogItems = await _db.CatalogItems.Where(x => ids.Contains(x.Id)).ToListAsync(cancellationToken);
        if (catalogItems.Count != ids.Length)
        {
            throw new NotificationApiException(400, "One or more catalog items do not exist.");
        }

        var orderItems = requestedLines.Select(line =>
        {
            var catalogItem = catalogItems.Single(x => x.Id == line.CatalogItemId);
            return new OrderItem(
                new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri),
                catalogItem.Price,
                line.Quantity);
        }).ToList();

        var address = new Address(
            request.ShippingAddress.Street,
            request.ShippingAddress.City,
            request.ShippingAddress.State ?? string.Empty,
            request.ShippingAddress.Country,
            request.ShippingAddress.ZipCode);
        var order = new Order(userId, address, orderItems);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);

        await SendForAllActiveContactsAsync(
            order,
            NotificationKind.OrderPlaced,
            $"Your eShop order #{order.Id} has been placed.",
            cancellationToken);

        return new PlaceOrderResponse(order.Id);
    }

    public async Task DispatchOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        if (order is null)
        {
            throw new NotificationApiException(404, "Order not found.");
        }

        bool changed;
        try
        {
            changed = order.MarkDispatched();
        }
        catch (InvalidOperationException)
        {
            throw new NotificationApiException(409, "A cancelled order cannot be dispatched.");
        }

        if (!changed)
        {
            return;
        }

        await _db.SaveChangesAsync(cancellationToken);
        var contacts = await ActiveContactsAsync(order.BuyerId, cancellationToken);
        foreach (var contact in contacts)
        {
            await CreateAndSendAsync(
                order,
                contact,
                NotificationKind.OrderDispatched,
                $"Your eShop order #{order.Id} is on its way.",
                null,
                cancellationToken);

            var sendAt = DateTimeOffset.UtcNow.AddDays(3);
            await CreateAndSendAsync(
                order,
                contact,
                NotificationKind.DeliveryFollowUp,
                $"How did delivery of your eShop order #{order.Id} go?",
                sendAt,
                cancellationToken);
        }
    }

    public async Task CancelOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        if (order is null)
        {
            throw new NotificationApiException(404, "Order not found.");
        }

        var changed = order.Cancel();
        await _db.SaveChangesAsync(cancellationToken);

        var followUps = await _db.OrderNotifications
            .Where(x => x.OrderId == order.Id
                        && x.Kind == NotificationKind.DeliveryFollowUp
                        && x.ProviderSid != null
                        && x.ProviderStatus != "canceled"
                        && x.ProviderDateSent == null)
            .ToListAsync(cancellationToken);
        foreach (var followUp in followUps)
        {
            await TryCancelScheduledAsync(followUp, cancellationToken);
        }
        await _db.SaveChangesAsync(cancellationToken);

        if (changed)
        {
            await SendForAllActiveContactsAsync(
                order,
                NotificationKind.OrderCancelled,
                $"Your eShop order #{order.Id} has been cancelled.",
                cancellationToken);
        }
    }

    public async Task<IReadOnlyList<MyOrderDto>> GetMyOrdersAsync(string userId, CancellationToken cancellationToken)
    {
        var orders = await _db.Orders
            .AsNoTracking()
            .Include(x => x.OrderItems)
            .Where(x => x.BuyerId == userId)
            .OrderByDescending(x => x.OrderDate)
            .ToListAsync(cancellationToken);
        var orderIds = orders.Select(x => x.Id).ToArray();
        var notifications = await _db.OrderNotifications
            .Where(x => orderIds.Contains(x.OrderId))
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        var refreshed = await RefreshAsync(notifications, cancellationToken);
        return orders.Select(order => new MyOrderDto(
            order.Id,
            order.OrderDate,
            order.Progress.ToString(),
            order.Total(),
            refreshed.Where(x => x.OrderId == order.Id).ToList())).ToList();
    }

    public async Task<IReadOnlyList<NotificationDto>> GetOrderNotificationsAsync(
        string userId,
        int orderId,
        CancellationToken cancellationToken)
    {
        var ownsOrder = await _db.Orders.AnyAsync(x => x.Id == orderId && x.BuyerId == userId, cancellationToken);
        if (!ownsOrder)
        {
            throw new NotificationApiException(404, "Order not found.");
        }

        var notifications = await _db.OrderNotifications
            .Where(x => x.OrderId == orderId && x.UserId == userId)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
        return await RefreshAsync(notifications, cancellationToken);
    }

    public async Task<ResendNotificationResponse> ResendAsync(
        int originalNotificationId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 128)
        {
            throw new NotificationApiException(400, "An idempotency key of at most 128 characters is required.");
        }

        var lockKey = $"{originalNotificationId}:{idempotencyKey}";
        var gate = ResendLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var prior = await _db.NotificationResendRequests.AsNoTracking().SingleOrDefaultAsync(
                x => x.OriginalNotificationId == originalNotificationId && x.IdempotencyKey == idempotencyKey,
                cancellationToken);
            if (prior is not null)
            {
                return new ResendNotificationResponse(prior.NotificationId);
            }

            var original = await _db.OrderNotifications.SingleOrDefaultAsync(x => x.Id == originalNotificationId, cancellationToken);
            if (original is null)
            {
                throw new NotificationApiException(404, "Notification not found.");
            }
            if (original.IsContentDisposed || string.IsNullOrWhiteSpace(original.Content))
            {
                throw new NotificationApiException(409, "Disposed notification content cannot be resent.");
            }

            if (original.ProviderSid is not null)
            {
                try
                {
                    ApplyProviderState(original, await _provider.FetchAsync(original.ProviderSid, cancellationToken));
                    await _db.SaveChangesAsync(cancellationToken);
                }
                catch (MessagingProviderException)
                {
                    // The stored last-known outcome remains authoritative for resend eligibility.
                }
            }

            if (!FailedStatuses.Contains(original.ProviderStatus ?? string.Empty)
                && original.Outcome is not ("send_failed" or "provider_rejected" or "unknown_outcome"))
            {
                throw new NotificationApiException(409, "Only a notification that did not reach the shopper can be resent.");
            }

            var contact = await _db.ContactNumbers.SingleOrDefaultAsync(
                x => x.Id == original.ContactNumberId && x.UserId == original.UserId && x.DeletedAt == null,
                cancellationToken);
            if (contact is null)
            {
                throw new NotificationApiException(409, "The destination is no longer registered.");
            }

            IDbContextTransaction? transaction = null;
            if (_db.Database.IsRelational())
            {
                transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
            }

            var resend = new OrderNotification(
                original.OrderId,
                contact.Id,
                original.UserId,
                NotificationKind.Resend,
                original.Content,
                originalNotificationId: original.Id);
            _db.OrderNotifications.Add(resend);
            await _db.SaveChangesAsync(cancellationToken);
            _db.NotificationResendRequests.Add(new NotificationResendRequest(original.Id, idempotencyKey, resend.Id));
            await _db.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                await transaction.DisposeAsync();
            }

            await TrySendImmediateAsync(resend, contact.CanonicalNumber, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
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
        var notification = await _db.OrderNotifications.SingleOrDefaultAsync(x => x.Id == notificationId, cancellationToken);
        if (notification is null)
        {
            throw new NotificationApiException(404, "Notification not found.");
        }
        if (notification.IsContentDisposed)
        {
            return;
        }

        if (notification.ProviderSid is not null)
        {
            ProviderMessage providerMessage;
            try
            {
                providerMessage = await _provider.RedactAsync(notification.ProviderSid, cancellationToken);
            }
            catch (MessagingProviderException)
            {
                throw new NotificationApiException(502, "The provider could not dispose of the message content.");
            }

            if (!string.IsNullOrEmpty(providerMessage.Body))
            {
                throw new NotificationApiException(502, "The provider did not confirm disposal of the message content.");
            }
            ApplyProviderState(notification, providerMessage);
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
            throw new NotificationApiException(400, "The from value must not be later than to.");
        }
        var widenedLower = new DateTimeOffset(from.UtcDateTime.Date.AddDays(-1), TimeSpan.Zero);
        var widenedUpper = new DateTimeOffset(to.UtcDateTime.Date.AddDays(1), TimeSpan.Zero);
        var providerMessages = new List<ProviderMessage>();
        var seenTokens = new HashSet<string>(StringComparer.Ordinal);
        string? pageToken = null;
        const int maxPages = 100;
        var complete = false;

        try
        {
            for (var page = 0; page < maxPages; page++)
            {
                var response = await _provider.ListAsync(widenedLower, widenedUpper, pageToken, cancellationToken);
                providerMessages.AddRange(response.Messages.Where(x =>
                    x.DateSent.HasValue && x.DateSent.Value >= from && x.DateSent.Value <= to));

                if (string.IsNullOrWhiteSpace(response.NextPageToken))
                {
                    complete = true;
                    break;
                }
                if (!seenTokens.Add(response.NextPageToken))
                {
                    break;
                }
                pageToken = response.NextPageToken;
            }
        }
        catch (MessagingProviderException)
        {
            throw new NotificationApiException(502, "The provider reconciliation report could not be completed.");
        }

        if (!complete)
        {
            throw new NotificationApiException(502, "The provider reconciliation report was incomplete.");
        }

        var local = await _db.OrderNotifications
            .AsNoTracking()
            .Where(x => x.CreatedAt >= from && x.CreatedAt <= to)
            .ToListAsync(cancellationToken);
        var localIds = local.Select(x => x.Id).ToHashSet();
        var providerSids = providerMessages
            .Where(x => !string.IsNullOrWhiteSpace(x.Sid))
            .Select(x => x.Sid!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        foreach (var sidBatch in providerSids.Chunk(500))
        {
            var matchedOutsideCreationRange = await _db.OrderNotifications
                .AsNoTracking()
                .Where(x => x.ProviderSid != null && sidBatch.Contains(x.ProviderSid))
                .ToListAsync(cancellationToken);
            foreach (var notification in matchedOutsideCreationRange)
            {
                if (localIds.Add(notification.Id))
                {
                    local.Add(notification);
                }
            }
        }
        var localBySid = local.Where(x => x.ProviderSid != null).ToDictionary(x => x.ProviderSid!, StringComparer.Ordinal);
        var matchedLocalIds = new HashSet<int>();
        var rows = new List<ReconciliationRow>();

        foreach (var providerMessage in providerMessages)
        {
            if (providerMessage.Sid is not null && localBySid.TryGetValue(providerMessage.Sid, out var notification))
            {
                matchedLocalIds.Add(notification.Id);
                rows.Add(new ReconciliationRow(
                    "matched",
                    providerMessage.Sid,
                    notification.Id,
                    providerMessage.Status,
                    notification.Outcome,
                    providerMessage.DateSent));
            }
            else
            {
                rows.Add(new ReconciliationRow(
                    "provider_only",
                    providerMessage.Sid,
                    null,
                    providerMessage.Status,
                    null,
                    providerMessage.DateSent));
            }
        }

        rows.AddRange(local
            .Where(x => !matchedLocalIds.Contains(x.Id))
            .Select(x => new ReconciliationRow(
                "application_only",
                x.ProviderSid,
                x.Id,
                x.ProviderStatus,
                x.Outcome,
                x.ProviderDateSent)));

        return new ReconciliationResponse(from, to, true, rows.OrderBy(x => x.ProviderDateSent).ThenBy(x => x.NotificationId).ToList());
    }

    public async Task RetryPendingCancellationsAsync(CancellationToken cancellationToken)
    {
        var pending = await _db.OrderNotifications
            .Where(x => x.CancellationRequested && x.ProviderSid != null && x.ScheduledFor > DateTimeOffset.UtcNow)
            .Take(100)
            .ToListAsync(cancellationToken);
        foreach (var notification in pending)
        {
            await TryCancelScheduledAsync(notification, cancellationToken);
        }
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task SendForAllActiveContactsAsync(
        Order order,
        NotificationKind kind,
        string content,
        CancellationToken cancellationToken)
    {
        var contacts = await ActiveContactsAsync(order.BuyerId, cancellationToken);
        foreach (var contact in contacts)
        {
            await CreateAndSendAsync(order, contact, kind, content, null, cancellationToken);
        }
    }

    private async Task<IReadOnlyList<ContactNumber>> ActiveContactsAsync(string userId, CancellationToken cancellationToken) =>
        await _db.ContactNumbers.Where(x => x.UserId == userId && x.DeletedAt == null).ToListAsync(cancellationToken);

    private async Task CreateAndSendAsync(
        Order order,
        ContactNumber contact,
        NotificationKind kind,
        string content,
        DateTimeOffset? scheduledFor,
        CancellationToken cancellationToken)
    {
        var notification = new OrderNotification(order.Id, contact.Id, order.BuyerId, kind, content, scheduledFor);
        _db.OrderNotifications.Add(notification);
        await _db.SaveChangesAsync(cancellationToken);

        if (!contact.IsActive)
        {
            notification.RecordFailure("destination_deleted");
        }
        else if (scheduledFor.HasValue)
        {
            await TryScheduleAsync(notification, contact.CanonicalNumber, scheduledFor.Value, cancellationToken);
        }
        else
        {
            await TrySendImmediateAsync(notification, contact.CanonicalNumber, cancellationToken);
        }
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task TrySendImmediateAsync(OrderNotification notification, string destination, CancellationToken cancellationToken)
    {
        try
        {
            ApplyProviderState(notification, await _provider.SendImmediateAsync(destination, notification.Content!, cancellationToken));
            if (notification.ProviderSid is null)
            {
                notification.RecordFailure("provider_response_incomplete");
            }
        }
        catch (MessagingProviderException ex)
        {
            notification.RecordFailure(IsCallerError(ex.StatusCode) ? "provider_rejected" : "unknown_outcome");
        }
    }

    private async Task TryScheduleAsync(
        OrderNotification notification,
        string destination,
        DateTimeOffset sendAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _provider.ScheduleAsync(destination, notification.Content!, sendAt, cancellationToken);
            ApplyProviderState(notification, response);
            if (notification.ProviderSid is null)
            {
                notification.RecordFailure("provider_response_incomplete");
            }
            else if (!string.IsNullOrWhiteSpace(response.From)
                     && !string.Equals(response.From, _settings.FromNumber, StringComparison.Ordinal))
            {
                notification.RequestCancellation();
                await TryCancelScheduledAsync(notification, cancellationToken);
                notification.RecordFailure("sender_mismatch");
            }
        }
        catch (MessagingProviderException ex)
        {
            notification.RecordFailure(IsCallerError(ex.StatusCode) ? "provider_rejected" : "unknown_outcome");
        }
    }

    private async Task<bool> TryCancelScheduledAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        if (notification.ProviderSid is null)
        {
            return true;
        }

        notification.RequestCancellation();
        try
        {
            var result = await _provider.CancelAsync(notification.ProviderSid, cancellationToken);
            ApplyProviderState(notification, result);
            if (string.Equals(result.Status, "canceled", StringComparison.OrdinalIgnoreCase))
            {
                notification.RecordCancellation();
                return true;
            }
        }
        catch (MessagingProviderException)
        {
            // The durable cancellation-pending state is retried by the hosted worker.
        }
        return false;
    }

    private async Task<IReadOnlyList<NotificationDto>> RefreshAsync(
        IReadOnlyList<OrderNotification> notifications,
        CancellationToken cancellationToken)
    {
        var result = new List<NotificationDto>(notifications.Count);
        foreach (var notification in notifications)
        {
            var stale = false;
            if (notification.ProviderSid is not null)
            {
                try
                {
                    ApplyProviderState(notification, await _provider.FetchAsync(notification.ProviderSid, cancellationToken));
                }
                catch (MessagingProviderException)
                {
                    stale = true;
                }
            }
            result.Add(ToDto(notification, stale));
        }
        await _db.SaveChangesAsync(cancellationToken);
        return result;
    }

    private static NotificationDto ToDto(OrderNotification notification, bool stale) =>
        new(
            notification.Id,
            notification.OrderId,
            notification.Kind.ToString(),
            notification.Outcome,
            notification.ProviderSid,
            notification.ProviderStatus,
            notification.ProviderErrorCode,
            notification.Content,
            notification.IsContentDisposed,
            notification.CreatedAt,
            notification.ScheduledFor,
            notification.ProviderDateSent,
            notification.LastRefreshedAt,
            stale);

    private static void ApplyProviderState(OrderNotification notification, ProviderMessage providerMessage)
    {
        notification.RecordProviderState(
            providerMessage.Sid,
            providerMessage.Status,
            providerMessage.ErrorCode,
            providerMessage.DateCreated,
            providerMessage.DateSent);
    }

    private static bool IsCallerError(HttpStatusCode? statusCode) =>
        statusCode.HasValue
        && (int)statusCode.Value >= 400
        && (int)statusCode.Value < 500
        && statusCode is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests);
}
