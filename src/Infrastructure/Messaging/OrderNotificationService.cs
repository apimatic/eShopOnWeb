using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public sealed class OrderNotificationService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ResendLocks = new(StringComparer.Ordinal);
    private static readonly TimeSpan NotificationBudget = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly CatalogContext _context;
    private readonly ITwilioMessagingGateway _twilio;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        CatalogContext context,
        ITwilioMessagingGateway twilio,
        TimeProvider timeProvider,
        ILogger<OrderNotificationService> logger)
    {
        _context = context;
        _twilio = twilio;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<int> RegisterContactNumberAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw new InvalidDestinationException();
        }

        var validation = await _twilio.ValidatePhoneNumberAsync(phoneNumber, cancellationToken);
        if (!validation.IsValid || string.IsNullOrWhiteSpace(validation.CanonicalNumber))
        {
            throw new InvalidDestinationException();
        }

        var canonical = validation.CanonicalNumber;
        var existing = await _context.ContactNumbers
            .AsNoTracking()
            .AnyAsync(number => number.BuyerId == buyerId && number.PhoneNumber == canonical && number.DeletedAt == null,
                cancellationToken);
        if (existing)
        {
            throw new ContactNumberAlreadyRegisteredException();
        }

        var contact = new ContactNumber(buyerId, canonical, UtcNow());
        _context.ContactNumbers.Add(contact);
        await _context.SaveChangesAsync(cancellationToken);
        return contact.Id;
    }

    public Task<List<ContactNumber>> GetContactNumbersAsync(string buyerId, CancellationToken cancellationToken) =>
        _context.ContactNumbers
            .AsNoTracking()
            .Where(number => number.BuyerId == buyerId && number.DeletedAt == null)
            .OrderBy(number => number.Id)
            .ToListAsync(cancellationToken);

    public async Task<bool> DeleteContactNumberAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken)
    {
        var contact = await _context.ContactNumbers
            .SingleOrDefaultAsync(number => number.Id == contactNumberId && number.BuyerId == buyerId && number.DeletedAt == null,
                cancellationToken);
        if (contact is null)
        {
            return false;
        }

        var now = UtcNow();
        contact.SoftDelete(now);
        var scheduled = await _context.OrderNotifications
            .Where(notification => notification.ContactNumberId == contactNumberId &&
                                   notification.ScheduledFor != null &&
                                   notification.CancellationCompletedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var notification in scheduled)
        {
            notification.RequestCancellation(now);
        }

        await _context.SaveChangesAsync(cancellationToken);
        await CancelScheduledNotificationsAsync(scheduled, cancellationToken);
        return true;
    }

    public async Task<int> PlaceOrderAsync(
        string buyerId,
        IReadOnlyCollection<OrderLineInput> requestedLines,
        ShippingAddressInput shippingAddress,
        CancellationToken cancellationToken)
    {
        if (requestedLines.Count == 0 || requestedLines.Any(line => line.CatalogItemId <= 0 || line.Quantity <= 0))
        {
            throw new InvalidOrderRequestException("At least one catalog item with a positive quantity is required.");
        }

        var combinedLines = requestedLines
            .GroupBy(line => line.CatalogItemId)
            .Select(group => new OrderLineInput(group.Key, checked(group.Sum(line => line.Quantity))))
            .ToList();
        var ids = combinedLines.Select(line => line.CatalogItemId).ToArray();
        var catalogItems = await _context.CatalogItems
            .AsNoTracking()
            .Where(item => ids.Contains(item.Id))
            .ToListAsync(cancellationToken);
        if (catalogItems.Count != ids.Length)
        {
            throw new InvalidOrderRequestException("One or more catalog items do not exist.");
        }

        var itemsById = catalogItems.ToDictionary(item => item.Id);
        var orderItems = combinedLines.Select(line =>
        {
            var item = itemsById[line.CatalogItemId];
            return new OrderItem(
                new CatalogItemOrdered(item.Id, item.Name, item.PictureUri),
                item.Price,
                line.Quantity);
        }).ToList();

        var address = new Address(
            Required(shippingAddress.Street, "street"),
            Required(shippingAddress.City, "city"),
            shippingAddress.State ?? string.Empty,
            Required(shippingAddress.Country, "country"),
            Required(shippingAddress.ZipCode, "zipCode"));
        var order = new Order(buyerId, address, orderItems);
        _context.Orders.Add(order);
        await _context.SaveChangesAsync(cancellationToken);

        await NotifyActiveContactsAsync(
            order,
            OrderNotificationKind.OrderPlaced,
            $"Your eShopOnWeb order #{order.Id} has been placed.",
            scheduledFor: null,
            cancellationToken);

        return order.Id;
    }

    public async Task<OrderTransitionResult> DispatchOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _context.Orders.SingleOrDefaultAsync(candidate => candidate.Id == orderId, cancellationToken);
        if (order is null)
        {
            return OrderTransitionResult.NotFound;
        }

        try
        {
            order.MarkDispatched(UtcNow());
        }
        catch (InvalidOperationException)
        {
            return OrderTransitionResult.Conflict;
        }

        await _context.SaveChangesAsync(cancellationToken);
        await NotifyActiveContactsAsync(
            order,
            OrderNotificationKind.OrderDispatched,
            $"Your eShopOnWeb order #{order.Id} has been dispatched and is on its way.",
            scheduledFor: null,
            cancellationToken);
        await NotifyActiveContactsAsync(
            order,
            OrderNotificationKind.DeliveryFollowUp,
            $"How did delivery of your eShopOnWeb order #{order.Id} go?",
            UtcNow().Add(FollowUpDelay),
            cancellationToken);

        return OrderTransitionResult.Success;
    }

    public async Task<OrderTransitionResult> CancelOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _context.Orders.SingleOrDefaultAsync(candidate => candidate.Id == orderId, cancellationToken);
        if (order is null)
        {
            return OrderTransitionResult.NotFound;
        }

        try
        {
            order.Cancel(UtcNow());
        }
        catch (InvalidOperationException)
        {
            return OrderTransitionResult.Conflict;
        }

        var now = UtcNow();
        var scheduled = await _context.OrderNotifications
            .Where(notification => notification.OrderId == orderId &&
                                   notification.ScheduledFor != null &&
                                   notification.CancellationCompletedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var notification in scheduled)
        {
            notification.RequestCancellation(now);
        }

        await _context.SaveChangesAsync(cancellationToken);
        await CancelScheduledNotificationsAsync(scheduled, cancellationToken);
        await NotifyActiveContactsAsync(
            order,
            OrderNotificationKind.OrderCancelled,
            $"Your eShopOnWeb order #{order.Id} has been cancelled.",
            scheduledFor: null,
            cancellationToken);

        return OrderTransitionResult.Success;
    }

    public async Task<List<Order>> GetOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        var orders = await _context.Orders
            .AsNoTracking()
            .Include(order => order.OrderItems)
            .Where(order => order.BuyerId == buyerId)
            .OrderByDescending(order => order.OrderDate)
            .ToListAsync(cancellationToken);
        var notifications = await _context.OrderNotifications
            .Where(notification => notification.BuyerId == buyerId)
            .ToListAsync(cancellationToken);
        await RefreshProviderStateAsync(notifications, cancellationToken);
        return orders;
    }

    public async Task<List<OrderNotification>?> GetOrderNotificationsAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var ownsOrder = await _context.Orders
            .AsNoTracking()
            .AnyAsync(order => order.Id == orderId && order.BuyerId == buyerId, cancellationToken);
        if (!ownsOrder)
        {
            return null;
        }

        var notifications = await _context.OrderNotifications
            .Where(notification => notification.OrderId == orderId && notification.BuyerId == buyerId)
            .OrderBy(notification => notification.CreatedAt)
            .ToListAsync(cancellationToken);
        await RefreshProviderStateAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<int?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 256)
        {
            throw new InvalidResendRequestException("An idempotency key of at most 256 characters is required.");
        }

        var lockKey = $"{notificationId}:{idempotencyKey}";
        var gate = ResendLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var existing = await _context.OrderNotifications
                .SingleOrDefaultAsync(notification => notification.SourceNotificationId == notificationId &&
                                                      notification.ResendIdempotencyKey == idempotencyKey,
                    cancellationToken);
            if (existing is not null)
            {
                return existing.Id;
            }

            var source = await _context.OrderNotifications
                .SingleOrDefaultAsync(notification => notification.Id == notificationId, cancellationToken);
            if (source is null)
            {
                return null;
            }

            var contact = await _context.ContactNumbers
                .SingleOrDefaultAsync(number => number.Id == source.ContactNumberId && number.DeletedAt == null,
                    cancellationToken);
            if (contact is null || string.IsNullOrWhiteSpace(source.Body) || HasReachedShopper(source.ProviderStatus))
            {
                throw new InvalidResendRequestException("Only an undelivered message with an active destination can be resent.");
            }

            var resend = new OrderNotification(
                source.OrderId,
                source.BuyerId,
                source.ContactNumberId,
                source.Kind,
                source.Body,
                UtcNow(),
                sourceNotificationId: source.Id,
                resendIdempotencyKey: idempotencyKey);
            _context.OrderNotifications.Add(resend);

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                _context.Entry(resend).State = EntityState.Detached;
                var concurrent = await _context.OrderNotifications
                    .AsNoTracking()
                    .SingleAsync(notification => notification.SourceNotificationId == notificationId &&
                                                 notification.ResendIdempotencyKey == idempotencyKey,
                        cancellationToken);
                return concurrent.Id;
            }

            await SendPersistedNotificationAsync(resend, contact.PhoneNumber, cancellationToken);
            return resend.Id;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<ContentDisposalResult> DisposeNotificationContentAsync(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _context.OrderNotifications
            .SingleOrDefaultAsync(candidate => candidate.Id == notificationId, cancellationToken);
        if (notification is null)
        {
            return ContentDisposalResult.NotFound;
        }

        if (notification.IsContentDisposed)
        {
            return ContentDisposalResult.Success;
        }

        if (!string.IsNullOrWhiteSpace(notification.ProviderMessageSid))
        {
            var provider = await _twilio.DisposeContentAsync(notification.ProviderMessageSid, cancellationToken);
            ApplyProviderState(notification, provider);
        }

        notification.DisposeContent(UtcNow());
        await _context.SaveChangesAsync(cancellationToken);
        return ContentDisposalResult.Success;
    }

    public async Task<IReadOnlyList<ReconciliationItem>> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (from > to)
        {
            throw new InvalidReconciliationRangeException();
        }

        var providerMessages = await _twilio.ListAsync(from, to, cancellationToken);
        var localNotifications = await _context.OrderNotifications
            .AsNoTracking()
            .Where(notification => notification.CreatedAt >= from && notification.CreatedAt <= to)
            .ToListAsync(cancellationToken);

        var localBySid = localNotifications
            .Where(notification => !string.IsNullOrWhiteSpace(notification.ProviderMessageSid))
            .ToDictionary(notification => notification.ProviderMessageSid!, StringComparer.Ordinal);
        var providerBySid = providerMessages.ToDictionary(message => message.Sid, StringComparer.Ordinal);
        var rows = new List<ReconciliationItem>();

        foreach (var provider in providerMessages)
        {
            localBySid.TryGetValue(provider.Sid, out var local);
            rows.Add(new ReconciliationItem(
                provider.Sid,
                local?.Id,
                local?.OrderId,
                provider.Status,
                local?.ProviderStatus,
                local is null ? ReconciliationMatch.ProviderOnly : ReconciliationMatch.Matched,
                provider.DateCreated ?? provider.DateSent));
        }

        foreach (var local in localNotifications.Where(notification =>
                     string.IsNullOrWhiteSpace(notification.ProviderMessageSid) ||
                     !providerBySid.ContainsKey(notification.ProviderMessageSid)))
        {
            rows.Add(new ReconciliationItem(
                local.ProviderMessageSid,
                local.Id,
                local.OrderId,
                null,
                local.ProviderStatus,
                ReconciliationMatch.LocalOnly,
                local.CreatedAt));
        }

        return rows.OrderBy(row => row.Timestamp).ThenBy(row => row.NotificationId).ToList();
    }

    private async Task NotifyActiveContactsAsync(
        Order order,
        OrderNotificationKind kind,
        string body,
        DateTimeOffset? scheduledFor,
        CancellationToken cancellationToken)
    {
        var contacts = await _context.ContactNumbers
            .Where(number => number.BuyerId == order.BuyerId && number.DeletedAt == null)
            .OrderBy(number => number.Id)
            .ToListAsync(cancellationToken);
        if (contacts.Count == 0)
        {
            return;
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(NotificationBudget);
        foreach (var contact in contacts)
        {
            var notification = new OrderNotification(
                order.Id,
                order.BuyerId,
                contact.Id,
                kind,
                body,
                UtcNow(),
                scheduledFor);
            _context.OrderNotifications.Add(notification);
            await _context.SaveChangesAsync(cancellationToken);
            await SendPersistedNotificationAsync(notification, contact.PhoneNumber, deadline.Token);
        }
    }

    private async Task SendPersistedNotificationAsync(
        OrderNotification notification,
        string destination,
        CancellationToken cancellationToken)
    {
        try
        {
            var provider = await _twilio.SendAsync(destination, notification.Body!, notification.ScheduledFor, cancellationToken);
            ApplyProviderState(notification, provider);
        }
        catch (TwilioProviderException ex)
        {
            if (ex.StatusCode is null)
            {
                notification.RecordProviderOutcomeUnknown(null, ex.Message, UtcNow());
            }
            else
            {
                notification.RecordProviderFailure((int)ex.StatusCode, ex.Message, UtcNow());
            }
            _logger.LogWarning("Twilio send failed for notification {NotificationId}; the order operation remains successful.", notification.Id);
        }
        catch (OperationCanceledException)
        {
            notification.RecordProviderOutcomeUnknown(null, "The messaging attempt exceeded its time budget.", UtcNow());
            _logger.LogWarning("Twilio send timed out for notification {NotificationId}; the order operation remains successful.", notification.Id);
        }

        await _context.SaveChangesAsync(CancellationToken.None);
    }

    private async Task CancelScheduledNotificationsAsync(
        IReadOnlyCollection<OrderNotification> notifications,
        CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrWhiteSpace(notification.ProviderMessageSid))
            {
                notification.MarkCanceled("not-created", UtcNow());
                continue;
            }

            try
            {
                var provider = await _twilio.CancelAsync(notification.ProviderMessageSid, cancellationToken);
                notification.MarkCanceled(provider.Status, UtcNow(), provider.DateUpdated);
            }
            catch (TwilioProviderException ex)
            {
                notification.RecordProviderFailure((int?)ex.StatusCode, ex.Message, UtcNow());
                _logger.LogWarning("Twilio cancellation is pending for notification {NotificationId} with provider message {ProviderMessageSid}.",
                    notification.Id, notification.ProviderMessageSid);
            }
            catch (OperationCanceledException)
            {
                notification.RecordProviderFailure(null, "The cancellation attempt exceeded its time budget.", UtcNow());
                _logger.LogWarning("Twilio cancellation timed out for notification {NotificationId} with provider message {ProviderMessageSid}.",
                    notification.Id, notification.ProviderMessageSid);
            }

            await _context.SaveChangesAsync(CancellationToken.None);
        }
    }

    private async Task RefreshProviderStateAsync(List<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        var changed = false;
        foreach (var notification in notifications.Where(candidate => !string.IsNullOrWhiteSpace(candidate.ProviderMessageSid)))
        {
            try
            {
                var provider = await _twilio.FetchAsync(notification.ProviderMessageSid!, cancellationToken);
                ApplyProviderState(notification, provider);
                changed = true;
            }
            catch (TwilioProviderException)
            {
                _logger.LogWarning("Twilio status refresh failed for notification {NotificationId}; the last observed outcome is retained.",
                    notification.Id);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        if (changed)
        {
            await _context.SaveChangesAsync(CancellationToken.None);
        }
    }

    private static void ApplyProviderState(OrderNotification notification, ProviderMessage provider) =>
        notification.UpdateProviderState(
            provider.Sid,
            provider.Status,
            provider.ErrorCode,
            provider.ErrorMessage,
            provider.DateCreated,
            provider.DateSent,
            provider.DateUpdated);

    private DateTimeOffset UtcNow() => _timeProvider.GetUtcNow();

    private static string Required(string? value, string fieldName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOrderRequestException($"Shipping address field '{fieldName}' is required.")
            : value;

    private static bool HasReachedShopper(string? providerStatus) =>
        providerStatus is not null &&
        (providerStatus.Equals("delivered", StringComparison.OrdinalIgnoreCase) ||
         providerStatus.Equals("read", StringComparison.OrdinalIgnoreCase) ||
         providerStatus.Equals("partially_delivered", StringComparison.OrdinalIgnoreCase));
}

public sealed record OrderLineInput(int CatalogItemId, int Quantity);

public sealed record ShippingAddressInput(string Street, string City, string? State, string Country, string ZipCode);

public enum OrderTransitionResult
{
    Success,
    NotFound,
    Conflict
}

public enum ContentDisposalResult
{
    Success,
    NotFound
}

public enum ReconciliationMatch
{
    Matched,
    ProviderOnly,
    LocalOnly
}

public sealed record ReconciliationItem(
    string? ProviderMessageSid,
    int? NotificationId,
    int? OrderId,
    string? ProviderStatus,
    string? LocalStatus,
    ReconciliationMatch Match,
    DateTimeOffset? Timestamp);

public sealed class InvalidDestinationException : Exception
{
    public InvalidDestinationException() : base("The phone number is not a usable messaging destination.") { }
}

public sealed class ContactNumberAlreadyRegisteredException : Exception
{
    public ContactNumberAlreadyRegisteredException() : base("The phone number is already registered.") { }
}

public sealed class InvalidOrderRequestException : Exception
{
    public InvalidOrderRequestException(string message) : base(message) { }
}

public sealed class InvalidResendRequestException : Exception
{
    public InvalidResendRequestException(string message) : base(message) { }
}

public sealed class InvalidReconciliationRangeException : Exception
{
    public InvalidReconciliationRangeException() : base("The 'from' date-time must not be later than 'to'.") { }
}
