using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

public sealed class NotificationWorkflowService
{
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);
    private readonly CatalogContext _context;
    private readonly ITwilioGateway _twilio;
    private readonly TimeProvider _clock;

    public NotificationWorkflowService(CatalogContext context, ITwilioGateway twilio, TimeProvider clock)
    {
        _context = context;
        _twilio = twilio;
        _clock = clock;
    }

    public async Task<ContactNumber> RegisterContactNumberAsync(
        string shopperId,
        string suppliedNumber,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(suppliedNumber))
        {
            throw new WorkflowValidationException("A phone number is required.");
        }

        PhoneNumberLookup lookup;
        try
        {
            lookup = await _twilio.ValidatePhoneNumberAsync(suppliedNumber, cancellationToken);
        }
        catch (TwilioApiException)
        {
            throw new WorkflowValidationException("The phone number could not be validated by the messaging provider.");
        }

        if (!lookup.IsValid || string.IsNullOrWhiteSpace(lookup.CanonicalNumber))
        {
            throw new WorkflowValidationException("The messaging provider does not consider this a valid destination.");
        }

        var existing = await _context.ContactNumbers.SingleOrDefaultAsync(
            x => x.ShopperId == shopperId && x.CanonicalNumber == lookup.CanonicalNumber,
            cancellationToken);
        if (existing is not null)
        {
            existing.Restore();
            await _context.SaveChangesAsync(cancellationToken);
            return existing;
        }

        var contact = new ContactNumber(shopperId, lookup.CanonicalNumber, UtcNow());
        _context.ContactNumbers.Add(contact);
        await _context.SaveChangesAsync(cancellationToken);
        return contact;
    }

    public Task<List<ContactNumber>> GetContactNumbersAsync(string shopperId, CancellationToken cancellationToken) =>
        _context.ContactNumbers
            .AsNoTracking()
            .Where(x => x.ShopperId == shopperId && x.DeletedAt == null)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);

    public async Task<bool> DeleteContactNumberAsync(string shopperId, int contactNumberId, CancellationToken cancellationToken)
    {
        var contact = await _context.ContactNumbers.SingleOrDefaultAsync(
            x => x.Id == contactNumberId && x.ShopperId == shopperId && x.DeletedAt == null,
            cancellationToken);
        if (contact is null)
        {
            return false;
        }

        var now = UtcNow();
        contact.Delete(now);
        var scheduled = await _context.OrderNotifications
            .Where(x => x.ContactNumberId == contact.Id &&
                        x.ScheduledFor != null &&
                        x.ProviderStatus != "canceled" &&
                        x.ProviderSentAt == null)
            .ToListAsync(cancellationToken);
        foreach (var notification in scheduled)
        {
            notification.RequestCancellation(now);
        }
        await _context.SaveChangesAsync(cancellationToken);

        foreach (var notification in scheduled)
        {
            await TryCancelAsync(notification, CancellationToken.None);
        }
        await _context.SaveChangesAsync(CancellationToken.None);
        return true;
    }

    public async Task<Order> PlaceOrderAsync(
        string shopperId,
        IReadOnlyCollection<OrderLineInput> requestedLines,
        ShippingAddressInput? requestedAddress,
        CancellationToken cancellationToken)
    {
        if (requestedLines.Count == 0 || requestedLines.Any(x => x.CatalogItemId <= 0 || x.Quantity <= 0))
        {
            throw new WorkflowValidationException("At least one catalog item with a positive quantity is required.");
        }

        var lines = requestedLines
            .GroupBy(x => x.CatalogItemId)
            .Select(x => new OrderLineInput(x.Key, x.Sum(y => y.Quantity)))
            .ToList();
        var itemIds = lines.Select(x => x.CatalogItemId).ToList();
        var catalogItems = await _context.CatalogItems
            .Where(x => itemIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        if (catalogItems.Count != itemIds.Count)
        {
            throw new WorkflowValidationException("One or more catalog items do not exist.");
        }

        var orderItems = lines.Select(line =>
        {
            var item = catalogItems[line.CatalogItemId];
            return new OrderItem(
                new CatalogItemOrdered(item.Id, item.Name, item.PictureUri),
                item.Price,
                line.Quantity);
        }).ToList();
        var address = requestedAddress is null
            ? new Address("Not provided", "Not provided", "Not provided", "Not provided", "Not provided")
            : new Address(
                RequiredAddressPart(requestedAddress.Street),
                RequiredAddressPart(requestedAddress.City),
                requestedAddress.State ?? string.Empty,
                RequiredAddressPart(requestedAddress.Country),
                RequiredAddressPart(requestedAddress.ZipCode));
        var order = new Order(shopperId, address, orderItems);
        _context.Orders.Add(order);
        await _context.SaveChangesAsync(cancellationToken);

        await NotifyActiveContactsAsync(
            order,
            NotificationKind.OrderPlaced,
            $"Your eShopOnWeb order #{order.Id} has been placed.",
            null,
            cancellationToken);
        return order;
    }

    public async Task<Order?> DispatchOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _context.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        bool transitioned;
        try
        {
            transitioned = order.Dispatch(UtcNow());
        }
        catch (InvalidOperationException ex)
        {
            throw new WorkflowConflictException(ex.Message);
        }
        await _context.SaveChangesAsync(cancellationToken);
        if (!transitioned)
        {
            return order;
        }

        await NotifyActiveContactsAsync(
            order,
            NotificationKind.OrderDispatched,
            $"Your eShopOnWeb order #{order.Id} has been dispatched and is on its way.",
            null,
            cancellationToken);
        await NotifyActiveContactsAsync(
            order,
            NotificationKind.DeliveryFollowUp,
            $"How did delivery of your eShopOnWeb order #{order.Id} go?",
            UtcNow().Add(FollowUpDelay),
            cancellationToken);
        return order;
    }

    public async Task<Order?> CancelOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _context.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        var now = UtcNow();
        var transitioned = order.Cancel(now);
        var scheduled = await _context.OrderNotifications
            .Where(x => x.OrderId == orderId &&
                        x.Kind == NotificationKind.DeliveryFollowUp &&
                        x.ProviderStatus != "canceled" &&
                        x.ProviderSentAt == null)
            .ToListAsync(cancellationToken);
        foreach (var notification in scheduled)
        {
            notification.RequestCancellation(now);
        }
        await _context.SaveChangesAsync(cancellationToken);

        foreach (var notification in scheduled)
        {
            await TryCancelAsync(notification, CancellationToken.None);
        }
        await _context.SaveChangesAsync(CancellationToken.None);

        if (transitioned)
        {
            await NotifyActiveContactsAsync(
                order,
                NotificationKind.OrderCancelled,
                $"Your eShopOnWeb order #{order.Id} has been cancelled.",
                null,
                cancellationToken);
        }
        return order;
    }

    public async Task<List<OrderNotification>> GetOrderNotificationsAsync(
        string shopperId,
        int orderId,
        CancellationToken cancellationToken)
    {
        var ownsOrder = await _context.Orders.AnyAsync(
            x => x.Id == orderId && x.BuyerId == shopperId,
            cancellationToken);
        if (!ownsOrder)
        {
            throw new WorkflowNotFoundException();
        }

        var notifications = await _context.OrderNotifications
            .Where(x => x.OrderId == orderId && x.ShopperId == shopperId)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        await RefreshProviderStatesAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<List<Order>> GetShopperOrdersAsync(string shopperId, CancellationToken cancellationToken)
    {
        return await _context.Orders
            .AsNoTracking()
            .Include(x => x.OrderItems)
            .Where(x => x.BuyerId == shopperId)
            .OrderByDescending(x => x.OrderDate)
            .ToListAsync(cancellationToken);
    }

    public async Task<Dictionary<int, List<OrderNotification>>> GetNotificationsForOrdersAsync(
        string shopperId,
        IReadOnlyCollection<int> orderIds,
        CancellationToken cancellationToken)
    {
        var notifications = await _context.OrderNotifications
            .Where(x => x.ShopperId == shopperId && orderIds.Contains(x.OrderId))
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        await RefreshProviderStatesAsync(notifications, cancellationToken);
        return notifications
            .GroupBy(x => x.OrderId)
            .ToDictionary(x => x.Key, x => x.ToList());
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 128)
        {
            throw new WorkflowValidationException("An idempotency key of at most 128 characters is required.");
        }

        var normalizedKey = idempotencyKey.Trim();
        var existing = await _context.OrderNotifications.SingleOrDefaultAsync(
            x => x.ResendOfNotificationId == notificationId && x.IdempotencyKey == normalizedKey,
            cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var source = await _context.OrderNotifications.SingleOrDefaultAsync(x => x.Id == notificationId, cancellationToken)
            ?? throw new WorkflowNotFoundException();
        await RefreshProviderStatesAsync(new[] { source }, cancellationToken);
        if (source.ProviderStatus is not ("failed" or "undelivered"))
        {
            throw new WorkflowConflictException("Only a failed or undelivered notification can be resent.");
        }
        if (source.Content is null)
        {
            throw new WorkflowConflictException("Disposed notification content cannot be resent.");
        }

        var contactIsActive = await _context.ContactNumbers.AnyAsync(
            x => x.Id == source.ContactNumberId && x.ShopperId == source.ShopperId && x.DeletedAt == null,
            cancellationToken);
        if (!contactIsActive)
        {
            throw new WorkflowConflictException("The destination is no longer registered.");
        }

        var orderIsCancelled = await _context.Orders.AnyAsync(
            x => x.Id == source.OrderId && x.Status == OrderStatus.Cancelled,
            cancellationToken);
        if (orderIsCancelled && source.Kind == NotificationKind.DeliveryFollowUp)
        {
            throw new WorkflowConflictException("A delivery follow-up cannot be resent for a cancelled order.");
        }

        var resend = new OrderNotification(
            source.OrderId,
            source.ContactNumberId,
            source.ShopperId,
            NotificationKind.Resend,
            source.Content,
            UtcNow(),
            resendOfNotificationId: source.Id,
            idempotencyKey: normalizedKey);
        _context.OrderNotifications.Add(resend);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _context.Entry(resend).State = EntityState.Detached;
            return await _context.OrderNotifications.SingleAsync(
                x => x.ResendOfNotificationId == notificationId && x.IdempotencyKey == normalizedKey,
                cancellationToken);
        }

        var contact = await _context.ContactNumbers.SingleAsync(x => x.Id == resend.ContactNumberId, cancellationToken);
        await TrySendAsync(resend, contact.CanonicalNumber, CancellationToken.None);
        await _context.SaveChangesAsync(CancellationToken.None);
        return resend;
    }

    public async Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _context.OrderNotifications.SingleOrDefaultAsync(x => x.Id == notificationId, cancellationToken);
        if (notification is null)
        {
            return false;
        }
        if (notification.ContentDisposedAt is not null)
        {
            return true;
        }

        if (notification.ProviderMessageSid is not null)
        {
            try
            {
                var provider = await _twilio.RedactMessageContentAsync(notification.ProviderMessageSid, cancellationToken);
                ApplyProviderState(notification, provider);
            }
            catch (TwilioApiException ex)
            {
                throw new WorkflowProviderException(ex.ProviderCode);
            }
        }

        notification.DisposeContent(UtcNow());
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<ReconciliationEntry>> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (from > to)
        {
            throw new WorkflowValidationException("The from date must not be after the to date.");
        }
        IReadOnlyList<TwilioMessage> providerMessages;
        try
        {
            providerMessages = await _twilio.ListMessagesAsync(from, to, cancellationToken);
        }
        catch (TwilioApiException ex)
        {
            throw new WorkflowProviderException(ex.ProviderCode);
        }

        var local = await _context.OrderNotifications
            .AsNoTracking()
            .Where(x => x.ProviderMessageSid != null &&
                        ((x.ProviderSentAt != null && x.ProviderSentAt >= from && x.ProviderSentAt <= to) ||
                         (x.ProviderSentAt == null && x.ProviderCreatedAt != null && x.ProviderCreatedAt >= from && x.ProviderCreatedAt <= to)))
            .ToListAsync(cancellationToken);
        var localBySid = local.ToDictionary(x => x.ProviderMessageSid!, StringComparer.Ordinal);
        var providerBySid = providerMessages.ToDictionary(x => x.Sid, StringComparer.Ordinal);
        var sids = localBySid.Keys.Union(providerBySid.Keys, StringComparer.Ordinal).OrderBy(x => x);

        return sids.Select(sid =>
        {
            localBySid.TryGetValue(sid, out var localMessage);
            providerBySid.TryGetValue(sid, out var providerMessage);
            return new ReconciliationEntry(
                sid,
                localMessage?.Id,
                localMessage is not null,
                providerMessage is not null,
                localMessage?.ProviderStatus,
                providerMessage?.Status,
                providerMessage?.DateSent,
                providerMessage?.ErrorCode);
        }).ToList();
    }

    public async Task RetryPendingCancellationsAsync(CancellationToken cancellationToken)
    {
        var pending = await _context.OrderNotifications
            .Where(x => x.CancellationRequestedAt != null &&
                        x.ProviderMessageSid != null &&
                        x.ProviderStatus != "canceled" &&
                        x.ProviderSentAt == null &&
                        x.ScheduledFor > DateTimeOffset.UtcNow)
            .Take(100)
            .ToListAsync(cancellationToken);
        foreach (var notification in pending)
        {
            await TryCancelAsync(notification, cancellationToken);
        }
        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task NotifyActiveContactsAsync(
        Order order,
        NotificationKind kind,
        string content,
        DateTimeOffset? scheduledFor,
        CancellationToken cancellationToken)
    {
        var contacts = await _context.ContactNumbers
            .Where(x => x.ShopperId == order.BuyerId && x.DeletedAt == null)
            .ToListAsync(cancellationToken);
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
            _context.OrderNotifications.Add(notification);
            await _context.SaveChangesAsync(cancellationToken);
            await TrySendAsync(notification, contact.CanonicalNumber, CancellationToken.None);
            await _context.SaveChangesAsync(CancellationToken.None);
        }
    }

    private async Task TrySendAsync(OrderNotification notification, string destination, CancellationToken cancellationToken)
    {
        try
        {
            var message = await _twilio.SendMessageAsync(destination, notification.Content!, notification.ScheduledFor, cancellationToken);
            ApplyProviderState(notification, message);
        }
        catch (TwilioApiException ex)
        {
            notification.RecordProviderFailure(ex.ProviderCode, UtcNow());
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            notification.RecordProviderFailure(null, UtcNow());
        }
    }

    private async Task TryCancelAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        if (notification.ProviderMessageSid is null)
        {
            return;
        }

        try
        {
            var current = await _twilio.GetMessageAsync(notification.ProviderMessageSid, cancellationToken);
            ApplyProviderState(notification, current);
            if (current.Status == "scheduled")
            {
                ApplyProviderState(notification, await _twilio.CancelScheduledMessageAsync(current.Sid, cancellationToken));
            }
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // The durable CancellationRequestedAt flag lets the hosted retry worker try again.
        }
    }

    private async Task RefreshProviderStatesAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        var changed = false;
        foreach (var notification in notifications.Where(x => x.ProviderMessageSid is not null))
        {
            try
            {
                var state = await _twilio.GetMessageAsync(notification.ProviderMessageSid!, cancellationToken);
                ApplyProviderState(notification, state);
                changed = true;
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // A read returns the last persisted outcome if Twilio is temporarily unavailable.
            }
        }
        if (changed)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    private void ApplyProviderState(OrderNotification notification, TwilioMessage message)
    {
        notification.RecordProviderState(
            message.Sid,
            message.Status,
            message.ErrorCode,
            message.DateCreated,
            message.DateSent,
            UtcNow());
    }

    private DateTimeOffset UtcNow() => _clock.GetUtcNow();
    private static string RequiredAddressPart(string? value) => string.IsNullOrWhiteSpace(value) ? "Not provided" : value;
}

public sealed record OrderLineInput(int CatalogItemId, int Quantity);
public sealed record ShippingAddressInput(string? Street, string? City, string? State, string? Country, string? ZipCode);
public sealed record ReconciliationEntry(
    string ProviderMessageSid,
    int? NotificationId,
    bool InApplication,
    bool InProvider,
    string? ApplicationStatus,
    string? ProviderStatus,
    DateTimeOffset? ProviderSentAt,
    int? ProviderErrorCode);

public sealed class WorkflowValidationException : Exception
{
    public WorkflowValidationException(string message) : base(message) { }
}

public sealed class WorkflowConflictException : Exception
{
    public WorkflowConflictException(string message) : base(message) { }
}

public sealed class WorkflowNotFoundException : Exception { }

public sealed class WorkflowProviderException : Exception
{
    public WorkflowProviderException(int? providerCode) : base("The messaging provider could not complete the request.")
    {
        ProviderCode = providerCode;
    }
    public int? ProviderCode { get; }
}
