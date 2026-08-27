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
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public sealed class OrderNotificationService
{
    private static readonly SemaphoreSlim ResendLock = new(1, 1);
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly CatalogContext _db;
    private readonly ISmsProvider _smsProvider;
    private readonly IUriComposer _uriComposer;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        CatalogContext db,
        ISmsProvider smsProvider,
        IUriComposer uriComposer,
        TimeProvider timeProvider,
        ILogger<OrderNotificationService> logger)
    {
        _db = db;
        _smsProvider = smsProvider;
        _uriComposer = uriComposer;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<ContactNumber> RegisterContactNumberAsync(
        string buyerId,
        string suppliedNumber,
        CancellationToken cancellationToken)
    {
        var validation = await _smsProvider.ValidatePhoneNumberAsync(suppliedNumber, cancellationToken);
        if (!validation.IsValid || string.IsNullOrWhiteSpace(validation.CanonicalNumber))
        {
            throw new NotificationValidationException("The phone number is not a valid SMS destination.");
        }

        var duplicate = await _db.ContactNumbers.AnyAsync(
            x => x.BuyerId == buyerId && x.CanonicalNumber == validation.CanonicalNumber && x.DeletedAt == null,
            cancellationToken);
        if (duplicate)
        {
            throw new NotificationConflictException("That contact number is already registered.");
        }

        var contact = new ContactNumber(buyerId, validation.CanonicalNumber, UtcNow());
        _db.ContactNumbers.Add(contact);
        await _db.SaveChangesAsync(cancellationToken);
        return contact;
    }

    public Task<List<ContactNumber>> GetContactNumbersAsync(string buyerId, CancellationToken cancellationToken) =>
        _db.ContactNumbers
            .AsNoTracking()
            .Where(x => x.BuyerId == buyerId && x.DeletedAt == null)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);

    public async Task<bool> DeleteContactNumberAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken)
    {
        var contact = await _db.ContactNumbers.SingleOrDefaultAsync(
            x => x.Id == contactNumberId && x.BuyerId == buyerId && x.DeletedAt == null,
            cancellationToken);
        if (contact is null)
        {
            return false;
        }

        var scheduled = await _db.OrderNotifications
            .Where(x => x.ContactNumberId == contactNumberId &&
                        x.Kind == NotificationKind.DeliveryFollowUp &&
                        x.ProviderMessageSid != null)
            .ToListAsync(cancellationToken);

        await RefreshNotificationsAsync(scheduled, cancellationToken, failOnProviderError: true);
        foreach (var notification in scheduled.Where(x => NotificationDeliveryStatus.IsScheduled(x.ProviderStatus)))
        {
            await CancelWithRetryAsync(notification, cancellationToken);
        }

        contact.Delete(UtcNow());
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<(Order Order, IReadOnlyList<OrderNotification> Notifications)> PlaceOrderAsync(
        string buyerId,
        IReadOnlyCollection<OrderLine> requestedLines,
        Address shippingAddress,
        CancellationToken cancellationToken)
    {
        if (requestedLines.Count == 0 || requestedLines.Any(x => x.CatalogItemId <= 0 || x.Quantity is <= 0 or > 100))
        {
            throw new NotificationValidationException("At least one catalog item with a quantity from 1 to 100 is required.");
        }

        var lines = requestedLines
            .GroupBy(x => x.CatalogItemId)
            .Select(x => new OrderLine(x.Key, x.Sum(y => y.Quantity)))
            .ToList();
        if (lines.Any(x => x.Quantity > 100))
        {
            throw new NotificationValidationException("The combined quantity for a catalog item cannot exceed 100.");
        }

        var ids = lines.Select(x => x.CatalogItemId).ToArray();
        var catalogItems = await _db.CatalogItems.Where(x => ids.Contains(x.Id)).ToListAsync(cancellationToken);
        if (catalogItems.Count != ids.Length)
        {
            throw new NotificationValidationException("One or more catalog items do not exist.");
        }

        var itemsById = catalogItems.ToDictionary(x => x.Id);
        var orderItems = lines.Select(line =>
        {
            var item = itemsById[line.CatalogItemId];
            return new OrderItem(
                new CatalogItemOrdered(item.Id, item.Name, _uriComposer.ComposePicUri(item.PictureUri)),
                item.Price,
                line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shippingAddress, orderItems);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);

        var notifications = await NotifyActiveContactsAsync(
            order,
            NotificationKind.OrderPlaced,
            $"Your eShopOnWeb order #{order.Id} has been placed.",
            scheduledFor: null,
            cancellationToken);
        return (order, notifications);
    }

    public async Task<(Order Order, IReadOnlyList<OrderNotification> Notifications)> DispatchOrderAsync(
        int orderId,
        CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        if (order.Status == OrderProgressStatus.Cancelled)
        {
            throw new NotificationConflictException("A cancelled order cannot be dispatched.");
        }

        if (order.Status == OrderProgressStatus.Dispatched)
        {
            var existing = await _db.OrderNotifications
                .Where(x => x.OrderId == orderId &&
                            (x.Kind == NotificationKind.OrderDispatched || x.Kind == NotificationKind.DeliveryFollowUp))
                .OrderBy(x => x.Id)
                .ToListAsync(cancellationToken);
            return (order, existing);
        }

        var now = UtcNow();
        order.Dispatch(now);
        await _db.SaveChangesAsync(cancellationToken);

        var dispatched = await NotifyActiveContactsAsync(
            order,
            NotificationKind.OrderDispatched,
            $"Your eShopOnWeb order #{order.Id} is on its way.",
            scheduledFor: null,
            cancellationToken);
        var followUp = await NotifyActiveContactsAsync(
            order,
            NotificationKind.DeliveryFollowUp,
            $"How did delivery of your eShopOnWeb order #{order.Id} go?",
            now.Add(FollowUpDelay),
            cancellationToken);

        return (order, dispatched.Concat(followUp).ToList());
    }

    public async Task<(Order Order, IReadOnlyList<OrderNotification> Notifications, int FollowUpCancellationFailures)> CancelOrderAsync(
        int orderId,
        CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        if (order.Status == OrderProgressStatus.Cancelled)
        {
            var existing = await _db.OrderNotifications
                .Where(x => x.OrderId == orderId && x.Kind == NotificationKind.OrderCancelled)
                .OrderBy(x => x.Id)
                .ToListAsync(cancellationToken);
            return (order, existing, 0);
        }

        var followUps = await _db.OrderNotifications
            .Where(x => x.OrderId == orderId &&
                        x.Kind == NotificationKind.DeliveryFollowUp &&
                        x.ProviderMessageSid != null)
            .ToListAsync(cancellationToken);
        await RefreshNotificationsAsync(followUps, cancellationToken);

        foreach (var notification in followUps.Where(x => NotificationDeliveryStatus.IsScheduled(x.ProviderStatus)))
        {
            try
            {
                await CancelWithRetryAsync(notification, cancellationToken);
            }
            catch (SmsProviderException ex)
            {
                notification.RecordCancellationFailure(ex.ProviderErrorCode, UtcNow());
                _logger.LogError(
                    "Provider cancellation failed for notification {NotificationId}; provider error code {ProviderErrorCode}.",
                    notification.Id,
                    ex.ProviderErrorCode);
                await _db.SaveChangesAsync(cancellationToken);
                throw;
            }
        }

        order.Cancel(UtcNow());
        await _db.SaveChangesAsync(cancellationToken);
        var notifications = await NotifyActiveContactsAsync(
            order,
            NotificationKind.OrderCancelled,
            $"Your eShopOnWeb order #{order.Id} has been cancelled.",
            scheduledFor: null,
            cancellationToken);
        return (order, notifications, 0);
    }

    public async Task<List<OrderSummary>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken)
    {
        var orders = await _db.Orders
            .AsNoTracking()
            .Include(x => x.OrderItems)
            .Where(x => x.BuyerId == buyerId)
            .OrderByDescending(x => x.OrderDate)
            .ToListAsync(cancellationToken);
        var orderIds = orders.Select(x => x.Id).ToArray();
        var notifications = await _db.OrderNotifications
            .Where(x => orderIds.Contains(x.OrderId))
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        await RefreshNotificationsAsync(notifications, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        return orders.Select(order => new OrderSummary(
            order,
            notifications.Where(x => x.OrderId == order.Id).ToList())).ToList();
    }

    public async Task<List<OrderNotification>?> GetOrderNotificationsForBuyerAsync(
        string buyerId,
        int orderId,
        CancellationToken cancellationToken)
    {
        var ownsOrder = await _db.Orders.AnyAsync(x => x.Id == orderId && x.BuyerId == buyerId, cancellationToken);
        if (!ownsOrder)
        {
            return null;
        }

        var notifications = await _db.OrderNotifications
            .Where(x => x.OrderId == orderId && x.BuyerId == buyerId)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        await RefreshNotificationsAsync(notifications, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return notifications;
    }

    public async Task<OrderNotification> ResendAsync(
        int notificationId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 128)
        {
            throw new NotificationValidationException("An idempotency key of 1 to 128 characters is required.");
        }

        var normalizedKey = idempotencyKey.Trim();
        await ResendLock.WaitAsync(cancellationToken);
        try
        {
            var prior = await _db.NotificationResendRequests
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    x => x.SourceNotificationId == notificationId && x.IdempotencyKey == normalizedKey,
                    cancellationToken);
            if (prior is not null)
            {
                return await _db.OrderNotifications.SingleAsync(x => x.Id == prior.ResultNotificationId, cancellationToken);
            }

            var source = await _db.OrderNotifications.SingleOrDefaultAsync(x => x.Id == notificationId, cancellationToken)
                ?? throw new NotificationNotFoundException("Notification not found.");
            if (source.ProviderMessageSid is not null)
            {
                await RefreshNotificationsAsync(new[] { source }, cancellationToken);
            }

            if (!NotificationDeliveryStatus.DidNotReachShopper(source.ProviderStatus))
            {
                throw new NotificationConflictException("Only a notification that failed or was undelivered can be resent.");
            }

            if (source.Kind == NotificationKind.DeliveryFollowUp)
            {
                throw new NotificationConflictException("A delivery follow-up cannot be resent manually.");
            }

            if (source.Content is null)
            {
                throw new NotificationConflictException("Redacted notification content cannot be resent.");
            }

            var contact = await _db.ContactNumbers.SingleOrDefaultAsync(
                x => x.Id == source.ContactNumberId && x.DeletedAt == null,
                cancellationToken);
            if (contact is null)
            {
                throw new NotificationConflictException("The notification's contact number is no longer registered.");
            }

            var result = new OrderNotification(
                source.OrderId,
                contact.Id,
                source.BuyerId,
                source.Kind,
                source.Content,
                UtcNow(),
                resendOfNotificationId: source.Id);
            _db.OrderNotifications.Add(result);
            await _db.SaveChangesAsync(cancellationToken);

            _db.NotificationResendRequests.Add(new NotificationResendRequest(
                source.Id,
                normalizedKey,
                result.Id,
                UtcNow()));
            await _db.SaveChangesAsync(cancellationToken);

            await SendNotificationAsync(result, contact.CanonicalNumber, cancellationToken);
            return result;
        }
        finally
        {
            ResendLock.Release();
        }
    }

    public async Task<bool> RedactContentAsync(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _db.OrderNotifications.SingleOrDefaultAsync(x => x.Id == notificationId, cancellationToken);
        if (notification is null)
        {
            return false;
        }

        if (notification.ContentRedactedAt.HasValue)
        {
            return true;
        }

        if (notification.ProviderMessageSid is not null)
        {
            var state = await _smsProvider.RedactMessageContentAsync(notification.ProviderMessageSid, cancellationToken);
            notification.RecordProviderState(state, UtcNow());
        }

        notification.Redact(UtcNow());
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<ReconciliationItem>> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (from >= to)
        {
            throw new NotificationValidationException("The 'from' date-time must be earlier than 'to'.");
        }

        var providerMessages = await _smsProvider.ListMessagesAsync(from, to, cancellationToken);
        var localNotifications = await _db.OrderNotifications
            .Where(x => (x.CreatedAt >= from && x.CreatedAt <= to) ||
                        (x.ProviderSentAt >= from && x.ProviderSentAt <= to))
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        var localBySid = localNotifications
            .Where(x => x.ProviderMessageSid != null)
            .ToDictionary(x => x.ProviderMessageSid!, StringComparer.Ordinal);
        var providerBySid = providerMessages.ToDictionary(x => x.Sid, StringComparer.Ordinal);

        foreach (var provider in providerMessages)
        {
            if (localBySid.TryGetValue(provider.Sid, out var local))
            {
                local.RecordProviderState(
                    new ProviderMessageState(provider.Sid, provider.Status, provider.ErrorCode, provider.DateCreated, provider.DateSent),
                    UtcNow());
            }
        }
        await _db.SaveChangesAsync(cancellationToken);

        var result = providerMessages.Select(provider =>
        {
            localBySid.TryGetValue(provider.Sid, out var local);
            return new ReconciliationItem(
                local?.Id,
                provider.Sid,
                local is null ? "provider_only" : "matched",
                local?.ProviderStatus,
                provider.Status,
                provider.ErrorCode,
                provider.DateCreated,
                provider.DateSent);
        }).ToList();

        result.AddRange(localNotifications
            .Where(local => local.ProviderMessageSid is null || !providerBySid.ContainsKey(local.ProviderMessageSid))
            .Select(local => new ReconciliationItem(
                local.Id,
                local.ProviderMessageSid,
                "application_only",
                local.ProviderStatus,
                null,
                local.ProviderErrorCode,
                local.ProviderCreatedAt,
                local.ProviderSentAt)));

        return result;
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken) =>
        await _db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken)
        ?? throw new NotificationNotFoundException("Order not found.");

    private async Task<IReadOnlyList<OrderNotification>> NotifyActiveContactsAsync(
        Order order,
        NotificationKind kind,
        string content,
        DateTimeOffset? scheduledFor,
        CancellationToken cancellationToken)
    {
        var contacts = await _db.ContactNumbers
            .Where(x => x.BuyerId == order.BuyerId && x.DeletedAt == null)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        var notifications = new List<OrderNotification>(contacts.Count);

        foreach (var contact in contacts)
        {
            var notification = new OrderNotification(
                order.Id,
                contact.Id,
                order.BuyerId,
                kind,
                content,
                UtcNow(),
                scheduledFor);
            _db.OrderNotifications.Add(notification);
            await _db.SaveChangesAsync(cancellationToken);
            await SendNotificationAsync(notification, contact.CanonicalNumber, cancellationToken);
            notifications.Add(notification);
        }

        return notifications;
    }

    private async Task SendNotificationAsync(
        OrderNotification notification,
        string canonicalNumber,
        CancellationToken cancellationToken)
    {
        try
        {
            var state = await _smsProvider.SendAsync(
                canonicalNumber,
                notification.Content!,
                notification.ScheduledFor,
                cancellationToken);
            notification.RecordProviderState(state, UtcNow());
        }
        catch (SmsProviderException ex)
        {
            notification.RecordProviderFailure(ex.ProviderErrorCode, UtcNow());
            _logger.LogWarning(
                "Provider send failed for notification {NotificationId}; provider error code {ProviderErrorCode}.",
                notification.Id,
                ex.ProviderErrorCode);
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task RefreshNotificationsAsync(
        IEnumerable<OrderNotification> notifications,
        CancellationToken cancellationToken,
        bool failOnProviderError = false)
    {
        foreach (var notification in notifications.Where(x => x.ProviderMessageSid is not null))
        {
            try
            {
                var state = await _smsProvider.GetMessageAsync(notification.ProviderMessageSid!, cancellationToken);
                notification.RecordProviderState(state, UtcNow());
            }
            catch (SmsProviderException ex)
            {
                _logger.LogWarning(
                    "Provider status refresh failed for notification {NotificationId}; provider error code {ProviderErrorCode}.",
                    notification.Id,
                    ex.ProviderErrorCode);
                if (failOnProviderError)
                {
                    throw;
                }
            }
        }
    }

    private async Task CancelWithRetryAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        SmsProviderException? lastError = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                var state = await _smsProvider.CancelScheduledMessageAsync(notification.ProviderMessageSid!, cancellationToken);
                notification.RecordProviderState(state, UtcNow());
                await _db.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (SmsProviderException ex)
            {
                lastError = ex;
                if (attempt < 3)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt), cancellationToken);
                }
            }
        }

        throw lastError!;
    }

    private DateTimeOffset UtcNow() => _timeProvider.GetUtcNow();
}

public sealed record OrderLine(int CatalogItemId, int Quantity);
public sealed record OrderSummary(Order Order, IReadOnlyList<OrderNotification> Notifications);
public sealed record ReconciliationItem(
    int? NotificationId,
    string? ProviderMessageSid,
    string Match,
    string? ApplicationStatus,
    string? ProviderStatus,
    int? ProviderErrorCode,
    DateTimeOffset? ProviderCreatedAt,
    DateTimeOffset? ProviderSentAt);

public sealed class NotificationValidationException : Exception
{
    public NotificationValidationException(string message) : base(message) { }
}

public sealed class NotificationConflictException : Exception
{
    public NotificationConflictException(string message) : base(message) { }
}

public sealed class NotificationNotFoundException : Exception
{
    public NotificationNotFoundException(string message) : base(message) { }
}
