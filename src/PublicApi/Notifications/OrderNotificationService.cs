using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

public sealed class OrderNotificationService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ResendLocks = new();
    private static readonly HashSet<string> ResendableStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "failed",
        "undelivered",
        NotificationProviderStatuses.SendFailed
    };
    private static readonly HashSet<string> NotYetSentStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "scheduled",
        "accepted",
        "queued"
    };

    private readonly CatalogContext _db;
    private readonly ITwilioMessagingClient _twilio;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        CatalogContext db,
        ITwilioMessagingClient twilio,
        TimeProvider timeProvider,
        ILogger<OrderNotificationService> logger)
    {
        _db = db;
        _twilio = twilio;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<ContactNumber> RegisterContactNumberAsync(
        string buyerId,
        string phoneNumber,
        string? countryCode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw new ApiValidationException("phoneNumber is required.");
        }

        PhoneNumberLookup lookup;
        try
        {
            lookup = await _twilio.LookupPhoneNumberAsync(phoneNumber, countryCode, cancellationToken);
        }
        catch (TwilioApiException)
        {
            throw new ProviderUnavailableException("The contact number could not be validated.");
        }
        catch (Exception)
        {
            throw new ProviderUnavailableException("The contact number could not be validated.");
        }

        if (!lookup.Valid || string.IsNullOrWhiteSpace(lookup.PhoneNumber))
        {
            throw new ApiValidationException("The messaging provider does not consider this a valid destination.");
        }

        var duplicate = await _db.ContactNumbers.AnyAsync(
            number => number.BuyerId == buyerId && number.Value == lookup.PhoneNumber && number.RemovedAt == null,
            cancellationToken);
        if (duplicate)
        {
            throw new ApiConflictException("That contact number is already registered.");
        }

        var contactNumber = new ContactNumber(buyerId, lookup.PhoneNumber, UtcNow());
        _db.ContactNumbers.Add(contactNumber);
        await _db.SaveChangesAsync(cancellationToken);
        return contactNumber;
    }

    public Task<List<ContactNumber>> GetContactNumbersAsync(string buyerId, CancellationToken cancellationToken) =>
        _db.ContactNumbers
            .AsNoTracking()
            .Where(number => number.BuyerId == buyerId && number.RemovedAt == null)
            .OrderBy(number => number.Id)
            .ToListAsync(cancellationToken);

    public async Task RemoveContactNumberAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken)
    {
        var contact = await _db.ContactNumbers.SingleOrDefaultAsync(
            number => number.Id == contactNumberId && number.BuyerId == buyerId && number.RemovedAt == null,
            cancellationToken);
        if (contact is null)
        {
            throw new ApiNotFoundException();
        }

        await CancelPendingFollowUpsAsync(contact.Id, orderId: null, requireProviderSuccess: true, cancellationToken);
        contact.Remove(UtcNow());
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyCollection<PlaceOrderItem> requestedItems,
        ShippingAddress shippingAddress,
        CancellationToken cancellationToken)
    {
        if (requestedItems.Count == 0 || requestedItems.Any(item => item.CatalogItemId <= 0 || item.Quantity <= 0))
        {
            throw new ApiValidationException("At least one catalog item with a positive quantity is required.");
        }

        ValidateAddress(shippingAddress);
        var quantities = requestedItems
            .GroupBy(item => item.CatalogItemId)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Quantity));
        var ids = quantities.Keys.ToList();
        var catalogItems = await _db.CatalogItems
            .AsNoTracking()
            .Where(item => ids.Contains(item.Id))
            .ToListAsync(cancellationToken);
        if (catalogItems.Count != ids.Count)
        {
            throw new ApiValidationException("One or more catalog items do not exist.");
        }

        var orderItems = catalogItems.Select(item => new OrderItem(
            new CatalogItemOrdered(item.Id, item.Name, item.PictureUri),
            item.Price,
            quantities[item.Id])).ToList();
        var address = new Address(
            shippingAddress.Street,
            shippingAddress.City,
            shippingAddress.State,
            shippingAddress.Country,
            shippingAddress.ZipCode);
        var order = new Order(buyerId, address, orderItems);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);

        var contacts = await ActiveContactsAsync(buyerId, cancellationToken);
        foreach (var contact in contacts)
        {
            await CreateAndSendAsync(
                order,
                contact,
                NotificationType.OrderPlaced,
                $"eShopOnWeb: Order #{order.Id} was placed.",
                scheduledFor: null,
                parentNotificationId: null,
                idempotencyKey: null,
                cancellationToken);
        }

        return order;
    }

    public async Task DispatchOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await FindOrderAsync(orderId, buyerId: null, cancellationToken);
        order.Dispatch(UtcNow());
        await _db.SaveChangesAsync(cancellationToken);

        var contacts = await ActiveContactsAsync(order.BuyerId, cancellationToken);
        foreach (var contact in contacts)
        {
            await CreateAndSendAsync(
                order,
                contact,
                NotificationType.OrderDispatched,
                $"eShopOnWeb: Order #{order.Id} is on its way.",
                scheduledFor: null,
                parentNotificationId: null,
                idempotencyKey: null,
                cancellationToken);

            var followUpAt = UtcNow().AddDays(3);
            await CreateAndSendAsync(
                order,
                contact,
                NotificationType.DeliveryFollowUp,
                $"eShopOnWeb: How did delivery of order #{order.Id} go?",
                followUpAt,
                parentNotificationId: null,
                idempotencyKey: null,
                cancellationToken);
        }
    }

    public async Task CancelOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await FindOrderAsync(orderId, buyerId: null, cancellationToken);
        order.Cancel(UtcNow());
        await _db.SaveChangesAsync(cancellationToken);

        await CancelPendingFollowUpsAsync(contactNumberId: null, order.Id, requireProviderSuccess: false, cancellationToken);
        var contacts = await ActiveContactsAsync(order.BuyerId, cancellationToken);
        foreach (var contact in contacts)
        {
            await CreateAndSendAsync(
                order,
                contact,
                NotificationType.OrderCanceled,
                $"eShopOnWeb: Order #{order.Id} was canceled.",
                scheduledFor: null,
                parentNotificationId: null,
                idempotencyKey: null,
                cancellationToken);
        }
    }

    public async Task<List<Order>> GetBuyerOrdersAsync(string buyerId, CancellationToken cancellationToken) =>
        await _db.Orders
            .AsNoTracking()
            .Include(order => order.OrderItems)
            .Where(order => order.BuyerId == buyerId)
            .OrderByDescending(order => order.OrderDate)
            .ToListAsync(cancellationToken);

    public async Task<List<OrderNotification>> GetOrderNotificationsAsync(
        int orderId,
        string buyerId,
        CancellationToken cancellationToken)
    {
        _ = await FindOrderAsync(orderId, buyerId, cancellationToken);
        var notifications = await _db.OrderNotifications
            .Where(notification => notification.OrderId == orderId && notification.BuyerId == buyerId)
            .OrderBy(notification => notification.CreatedAt)
            .ToListAsync(cancellationToken);
        await RefreshNotificationsAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<Dictionary<int, List<OrderNotification>>> GetNotificationSummariesAsync(
        string buyerId,
        CancellationToken cancellationToken)
    {
        var notifications = await _db.OrderNotifications
            .Where(notification => notification.BuyerId == buyerId)
            .OrderBy(notification => notification.CreatedAt)
            .ToListAsync(cancellationToken);
        await RefreshNotificationsAsync(notifications, cancellationToken);
        return notifications.GroupBy(notification => notification.OrderId)
            .ToDictionary(group => group.Key, group => group.ToList());
    }

    public async Task<OrderNotification> ResendNotificationAsync(
        int notificationId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 128)
        {
            throw new ApiValidationException("An idempotencyKey of at most 128 characters is required.");
        }

        var lockKey = $"{notificationId}:{idempotencyKey}";
        var resendLock = ResendLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await resendLock.WaitAsync(cancellationToken);
        try
        {
            var existing = await _db.OrderNotifications.SingleOrDefaultAsync(
                notification => notification.ParentNotificationId == notificationId &&
                                notification.IdempotencyKey == idempotencyKey,
                cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            var original = await _db.OrderNotifications.SingleOrDefaultAsync(
                notification => notification.Id == notificationId,
                cancellationToken) ?? throw new ApiNotFoundException();
            await RefreshNotificationAsync(original, cancellationToken);
            if (!ResendableStatuses.Contains(original.ProviderStatus))
            {
                throw new ApiConflictException("Only a failed or undelivered notification can be resent.");
            }

            if (original.IsContentRedacted || string.IsNullOrWhiteSpace(original.Body))
            {
                throw new ApiConflictException("Disposed notification content cannot be resent.");
            }

            var contact = await _db.ContactNumbers.SingleOrDefaultAsync(
                number => number.Id == original.ContactNumberId && number.RemovedAt == null,
                cancellationToken);
            if (contact is null)
            {
                throw new ApiConflictException("The destination is no longer registered.");
            }

            var resend = new OrderNotification(
                original.OrderId,
                original.ContactNumberId,
                original.BuyerId,
                NotificationType.Resend,
                original.Body,
                UtcNow(),
                parentNotificationId: original.Id,
                idempotencyKey: idempotencyKey);
            _db.OrderNotifications.Add(resend);
            await _db.SaveChangesAsync(cancellationToken);
            await SendAttemptAsync(resend, contact.Value, cancellationToken);
            return resend;
        }
        finally
        {
            resendLock.Release();
            ResendLocks.TryRemove(lockKey, out _);
        }
    }

    public async Task DisposeNotificationContentAsync(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _db.OrderNotifications.SingleOrDefaultAsync(
            item => item.Id == notificationId,
            cancellationToken) ?? throw new ApiNotFoundException();
        if (notification.IsContentRedacted)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(notification.ProviderMessageSid))
        {
            TwilioMessage provider;
            try
            {
                provider = await _twilio.RedactMessageAsync(notification.ProviderMessageSid, cancellationToken);
            }
            catch (Exception)
            {
                throw new ProviderUnavailableException("The provider could not dispose of the notification content.");
            }
            if (!string.IsNullOrEmpty(provider.Body))
            {
                throw new ProviderUnavailableException("The provider did not confirm notification content disposal.");
            }
            notification.RecordProviderState(
                provider.Sid,
                provider.Status,
                provider.ErrorCode,
                provider.DateCreated,
                provider.DateSent,
                UtcNow());
        }

        notification.Redact(UtcNow());
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ReconciliationEntry>> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (from >= to)
        {
            throw new ApiValidationException("from must be earlier than to.");
        }

        var providerMessages = await _twilio.ListMessagesAsync(from, to, cancellationToken);
        var local = await _db.OrderNotifications
            .Where(notification => notification.CreatedAt >= from && notification.CreatedAt <= to)
            .ToListAsync(cancellationToken);
        var localBySid = local
            .Where(notification => !string.IsNullOrWhiteSpace(notification.ProviderMessageSid))
            .ToDictionary(notification => notification.ProviderMessageSid!, StringComparer.Ordinal);
        var result = new List<ReconciliationEntry>();

        foreach (var provider in providerMessages)
        {
            if (localBySid.Remove(provider.Sid, out var notification))
            {
                notification.RecordProviderState(
                    provider.Sid,
                    provider.Status,
                    provider.ErrorCode,
                    provider.DateCreated,
                    provider.DateSent,
                    UtcNow());
                result.Add(new ReconciliationEntry(
                    "matched",
                    notification.Id,
                    provider.Sid,
                    notification.ProviderStatus,
                    provider.Status,
                    provider.DateSent ?? provider.DateCreated));
            }
            else
            {
                result.Add(new ReconciliationEntry(
                    "provider-only",
                    null,
                    provider.Sid,
                    null,
                    provider.Status,
                    provider.DateSent ?? provider.DateCreated));
            }
        }

        var matchedIds = result.Where(entry => entry.NotificationId.HasValue)
            .Select(entry => entry.NotificationId!.Value)
            .ToHashSet();
        result.AddRange(local
            .Where(notification => !matchedIds.Contains(notification.Id))
            .Select(notification => new ReconciliationEntry(
                "application-only",
                notification.Id,
                notification.ProviderMessageSid,
                notification.ProviderStatus,
                null,
                notification.ProviderDateSent ?? notification.ProviderDateCreated ?? notification.CreatedAt)));
        await _db.SaveChangesAsync(cancellationToken);
        return result.OrderBy(entry => entry.Timestamp).ToList();
    }

    private async Task<OrderNotification> CreateAndSendAsync(
        Order order,
        ContactNumber contact,
        NotificationType type,
        string body,
        DateTimeOffset? scheduledFor,
        int? parentNotificationId,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var notification = new OrderNotification(
            order.Id,
            contact.Id,
            order.BuyerId,
            type,
            body,
            UtcNow(),
            scheduledFor,
            parentNotificationId,
            idempotencyKey);
        _db.OrderNotifications.Add(notification);
        await _db.SaveChangesAsync(cancellationToken);
        await SendAttemptAsync(notification, contact.Value, cancellationToken);
        return notification;
    }

    private async Task SendAttemptAsync(
        OrderNotification notification,
        string destination,
        CancellationToken cancellationToken)
    {
        try
        {
            var provider = await _twilio.SendMessageAsync(destination, notification.Body!, notification.ScheduledFor, cancellationToken);
            notification.RecordProviderState(
                provider.Sid,
                provider.Status,
                provider.ErrorCode,
                provider.DateCreated,
                provider.DateSent,
                UtcNow());
        }
        catch (TwilioApiException exception)
        {
            notification.RecordSendFailure(exception.ProviderErrorCode, UtcNow());
            _logger.LogWarning(
                "Provider rejected notification {NotificationId} with code {ProviderErrorCode}.",
                notification.Id,
                exception.ProviderErrorCode);
        }
        catch (Exception)
        {
            notification.RecordSendFailure(null, UtcNow());
            _logger.LogWarning("Provider request failed for notification {NotificationId}.", notification.Id);
        }

        await _db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task RefreshNotificationsAsync(
        IEnumerable<OrderNotification> notifications,
        CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            await RefreshNotificationAsync(notification, cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task RefreshNotificationAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(notification.ProviderMessageSid))
        {
            return;
        }

        try
        {
            var provider = await _twilio.FetchMessageAsync(notification.ProviderMessageSid, cancellationToken);
            notification.RecordProviderState(
                provider.Sid,
                provider.Status,
                provider.ErrorCode,
                provider.DateCreated,
                provider.DateSent,
                UtcNow());
        }
        catch (Exception)
        {
            _logger.LogWarning("Provider status refresh failed for notification {NotificationId}.", notification.Id);
        }
    }

    private async Task CancelPendingFollowUpsAsync(
        int? contactNumberId,
        int? orderId,
        bool requireProviderSuccess,
        CancellationToken cancellationToken)
    {
        var query = _db.OrderNotifications.Where(notification => notification.Type == NotificationType.DeliveryFollowUp);
        if (contactNumberId.HasValue)
        {
            query = query.Where(notification => notification.ContactNumberId == contactNumberId.Value);
        }

        if (orderId.HasValue)
        {
            query = query.Where(notification => notification.OrderId == orderId.Value);
        }

        var followUps = await query.ToListAsync(cancellationToken);
        var providerFailure = false;
        foreach (var followUp in followUps)
        {
            if (string.IsNullOrWhiteSpace(followUp.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                await RefreshNotificationAsync(followUp, cancellationToken);
                if (!NotYetSentStatuses.Contains(followUp.ProviderStatus))
                {
                    continue;
                }

                var canceled = await _twilio.CancelMessageAsync(followUp.ProviderMessageSid, cancellationToken);
                followUp.RecordProviderState(
                    canceled.Sid,
                    canceled.Status,
                    canceled.ErrorCode,
                    canceled.DateCreated,
                    canceled.DateSent,
                    UtcNow());
            }
            catch (Exception)
            {
                providerFailure = true;
                _logger.LogWarning("Provider cancellation failed for notification {NotificationId}.", followUp.Id);
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        if (providerFailure && requireProviderSuccess)
        {
            throw new ProviderUnavailableException("Pending messages could not be canceled; the contact number was not removed.");
        }
    }

    private Task<List<ContactNumber>> ActiveContactsAsync(string buyerId, CancellationToken cancellationToken) =>
        _db.ContactNumbers
            .Where(number => number.BuyerId == buyerId && number.RemovedAt == null)
            .OrderBy(number => number.Id)
            .ToListAsync(cancellationToken);

    private async Task<Order> FindOrderAsync(int orderId, string? buyerId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders.SingleOrDefaultAsync(
            candidate => candidate.Id == orderId && (buyerId == null || candidate.BuyerId == buyerId),
            cancellationToken);
        return order ?? throw new ApiNotFoundException();
    }

    private static void ValidateAddress(ShippingAddress address)
    {
        if (string.IsNullOrWhiteSpace(address.Street) ||
            string.IsNullOrWhiteSpace(address.City) ||
            string.IsNullOrWhiteSpace(address.Country) ||
            string.IsNullOrWhiteSpace(address.ZipCode))
        {
            throw new ApiValidationException("A complete shipping address is required.");
        }
    }

    private DateTimeOffset UtcNow() => _timeProvider.GetUtcNow();
}

public sealed record PlaceOrderItem(int CatalogItemId, int Quantity);

public sealed record ShippingAddress(string Street, string City, string State, string Country, string ZipCode);

public sealed record ReconciliationEntry(
    string Match,
    int? NotificationId,
    string? ProviderMessageSid,
    string? ApplicationStatus,
    string? ProviderStatus,
    DateTimeOffset? Timestamp);

public sealed class ApiValidationException : Exception
{
    public ApiValidationException(string message) : base(message) { }
}

public sealed class ApiConflictException : Exception
{
    public ApiConflictException(string message) : base(message) { }
}

public sealed class ApiNotFoundException : Exception { }

public sealed class ProviderUnavailableException : Exception
{
    public ProviderUnavailableException(string message) : base(message) { }
}
