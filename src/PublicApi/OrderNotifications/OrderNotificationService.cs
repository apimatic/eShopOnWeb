using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.PublicApi.OrderNotifications;

public sealed class OrderNotificationService
{
    private static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);
    private readonly CatalogContext _db;
    private readonly ISmsProvider _smsProvider;

    public OrderNotificationService(CatalogContext db, ISmsProvider smsProvider)
    {
        _db = db;
        _smsProvider = smsProvider;
    }

    public async Task<ContactRegistrationResult> RegisterContactNumberAsync(
        string buyerId,
        string rawNumber,
        string? countryCode,
        CancellationToken cancellationToken)
    {
        var validation = await _smsProvider.ValidateDestinationAsync(rawNumber, countryCode, cancellationToken);
        if (!validation.IsValid || string.IsNullOrWhiteSpace(validation.CanonicalNumber))
        {
            return new ContactRegistrationResult(null, validation.Errors);
        }

        var existing = await _db.ContactNumbers.SingleOrDefaultAsync(
            x => x.BuyerId == buyerId && x.Value == validation.CanonicalNumber,
            cancellationToken);
        if (existing != null)
        {
            return new ContactRegistrationResult(existing, Array.Empty<string>());
        }

        var contact = new ContactNumber(buyerId, validation.CanonicalNumber, DateTimeOffset.UtcNow);
        _db.ContactNumbers.Add(contact);
        await _db.SaveChangesAsync(cancellationToken);
        return new ContactRegistrationResult(contact, Array.Empty<string>());
    }

    public Task<List<ContactNumber>> GetContactNumbersAsync(string buyerId, CancellationToken cancellationToken)
        => _db.ContactNumbers
            .AsNoTracking()
            .Where(x => x.BuyerId == buyerId)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);

    public async Task<bool> DeleteContactNumberAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken)
    {
        var contact = await _db.ContactNumbers.SingleOrDefaultAsync(
            x => x.Id == contactNumberId && x.BuyerId == buyerId,
            cancellationToken);
        if (contact == null)
        {
            return false;
        }

        var scheduled = await _db.OrderNotifications
            .Where(x => x.ContactNumberId == contactNumberId &&
                        x.ProviderMessageSid != null &&
                        x.Kind == NotificationKind.DeliveryFollowUp &&
                        x.ProviderStatus != "canceled")
            .ToListAsync(cancellationToken);
        foreach (var notification in scheduled)
        {
            var current = await _smsProvider.GetMessageAsync(notification.ProviderMessageSid!, cancellationToken);
            notification.RefreshProviderState(
                current.Status,
                current.ErrorCode,
                current.Body == string.Empty,
                DateTimeOffset.UtcNow);
            if (current.Status == "scheduled")
            {
                var canceled = await _smsProvider.CancelMessageAsync(notification.ProviderMessageSid!, cancellationToken);
                notification.RefreshProviderState(canceled.Status, canceled.ErrorCode, canceled.Body == string.Empty, DateTimeOffset.UtcNow);
            }
        }

        _db.ContactNumbers.Remove(contact);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyCollection<OrderLineInput> requestedLines,
        AddressInput address,
        CancellationToken cancellationToken)
    {
        if (requestedLines.Count == 0 || requestedLines.Any(x => x.CatalogItemId <= 0 || x.Quantity <= 0))
        {
            throw new OrderInputException("At least one catalog item with a positive quantity is required.");
        }

        var quantities = requestedLines
            .GroupBy(x => x.CatalogItemId)
            .ToDictionary(x => x.Key, x => x.Sum(line => line.Quantity));
        if (quantities.Values.Any(x => x > 1000))
        {
            throw new OrderInputException("An item quantity cannot exceed 1000.");
        }

        var items = await _db.CatalogItems
            .Where(x => quantities.Keys.Contains(x.Id))
            .ToListAsync(cancellationToken);
        if (items.Count != quantities.Count)
        {
            var missing = quantities.Keys.Except(items.Select(x => x.Id)).OrderBy(x => x);
            throw new OrderInputException($"Unknown catalog item ids: {string.Join(", ", missing)}.");
        }

        ValidateAddress(address);
        var orderItems = items.Select(item => new OrderItem(
            new CatalogItemOrdered(item.Id, item.Name, item.PictureUri),
            item.Price,
            quantities[item.Id])).ToList();
        var order = new Order(
            buyerId,
            new Address(address.Street, address.City, address.State, address.Country, address.ZipCode),
            orderItems);

        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);
        await NotifyAllAsync(
            order,
            NotificationKind.OrderPlaced,
            $"eShopOnWeb: Order #{order.Id} was placed successfully.",
            null,
            cancellationToken);
        return order;
    }

    public async Task<Order?> DispatchOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        if (order == null)
        {
            return null;
        }

        if (!order.Dispatch(DateTimeOffset.UtcNow))
        {
            return order;
        }

        await _db.SaveChangesAsync(cancellationToken);
        await NotifyAllAsync(
            order,
            NotificationKind.OrderDispatched,
            $"eShopOnWeb: Order #{order.Id} has been dispatched and is on its way.",
            null,
            cancellationToken);
        await NotifyAllAsync(
            order,
            NotificationKind.DeliveryFollowUp,
            $"eShopOnWeb: How did delivery of order #{order.Id} go?",
            DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay),
            cancellationToken);
        return order;
    }

    public async Task<Order?> CancelOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        if (order == null)
        {
            return null;
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            return order;
        }

        var followUps = await _db.OrderNotifications
            .Where(x => x.OrderId == orderId &&
                        x.Kind == NotificationKind.DeliveryFollowUp &&
                        x.ProviderMessageSid != null &&
                        x.ProviderStatus != "canceled")
            .ToListAsync(cancellationToken);
        foreach (var followUp in followUps)
        {
            var current = await _smsProvider.GetMessageAsync(followUp.ProviderMessageSid!, cancellationToken);
            followUp.RefreshProviderState(current.Status, current.ErrorCode, current.Body == string.Empty, DateTimeOffset.UtcNow);
            if (current.Status == "scheduled")
            {
                var canceled = await _smsProvider.CancelMessageAsync(followUp.ProviderMessageSid!, cancellationToken);
                followUp.RefreshProviderState(canceled.Status, canceled.ErrorCode, canceled.Body == string.Empty, DateTimeOffset.UtcNow);
            }
            else if (current.Status is not ("canceled" or "failed" or "undelivered"))
            {
                throw new FollowUpCancellationException("The delivery follow-up can no longer be canceled safely.");
            }
        }

        order.Cancel(DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);
        await NotifyAllAsync(
            order,
            NotificationKind.OrderCancelled,
            $"eShopOnWeb: Order #{order.Id} has been cancelled.",
            null,
            cancellationToken);
        return order;
    }

    public async Task<List<OrderSummary>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        var orders = await _db.Orders
            .AsNoTracking()
            .Include(x => x.OrderItems)
            .Where(x => x.BuyerId == buyerId)
            .OrderByDescending(x => x.OrderDate)
            .ToListAsync(cancellationToken);
        var orderIds = orders.Select(x => x.Id).ToList();
        var notifications = await _db.OrderNotifications
            .Where(x => orderIds.Contains(x.OrderId))
            .ToListAsync(cancellationToken);
        await RefreshNotificationsAsync(notifications, cancellationToken);

        return orders.Select(order => new OrderSummary(
            order.Id,
            order.OrderDate,
            order.Status.ToString(),
            order.Total(),
            notifications.Where(x => x.OrderId == order.Id)
                .GroupBy(x => x.ProviderStatus)
                .ToDictionary(x => x.Key, x => x.Count())))
            .ToList();
    }

    public async Task<List<OrderNotification>?> GetOrderNotificationsAsync(
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
        return notifications;
    }

    public async Task<ResendResult?> ResendAsync(int sourceNotificationId, string idempotencyKey, CancellationToken cancellationToken)
    {
        var normalizedKey = idempotencyKey?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedKey) || normalizedKey.Length > 128)
        {
            throw new OrderInputException("An idempotency key of 1 to 128 characters is required.");
        }

        var existing = await _db.OrderNotifications.SingleOrDefaultAsync(
            x => x.ResendOfNotificationId == sourceNotificationId && x.IdempotencyKey == normalizedKey,
            cancellationToken);
        if (existing != null)
        {
            return new ResendResult(existing, false);
        }

        var source = await _db.OrderNotifications.SingleOrDefaultAsync(x => x.Id == sourceNotificationId, cancellationToken);
        if (source == null)
        {
            return null;
        }

        if (source.ProviderMessageSid != null)
        {
            await RefreshNotificationAsync(source, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
        }

        if (source.ProviderStatus is not ("failed" or "undelivered" or NotificationDeliveryStatus.ProviderRejected))
        {
            throw new ResendNotAllowedException("Only a failed or undelivered notification can be resent.");
        }
        if (string.IsNullOrWhiteSpace(source.Body))
        {
            throw new ResendNotAllowedException("Disposed notification content cannot be resent.");
        }

        var contactStillRegistered = await _db.ContactNumbers.AnyAsync(
            x => x.Id == source.ContactNumberId && x.BuyerId == source.BuyerId && x.Value == source.Destination,
            cancellationToken);
        if (!contactStillRegistered)
        {
            throw new ResendNotAllowedException("The destination is no longer registered.");
        }

        var order = await _db.Orders.SingleAsync(x => x.Id == source.OrderId, cancellationToken);
        if (order.Status == OrderStatus.Cancelled && source.Kind != NotificationKind.OrderCancelled)
        {
            throw new ResendNotAllowedException("This notification no longer reflects the cancelled order.");
        }

        var resend = new OrderNotification(
            source.OrderId,
            source.BuyerId,
            source.ContactNumberId,
            source.Destination,
            NotificationKind.Resend,
            source.Body,
            DateTimeOffset.UtcNow,
            source.Id,
            normalizedKey);
        _db.OrderNotifications.Add(resend);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _db.Entry(resend).State = EntityState.Detached;
            existing = await _db.OrderNotifications.SingleOrDefaultAsync(
                x => x.ResendOfNotificationId == sourceNotificationId && x.IdempotencyKey == normalizedKey,
                cancellationToken);
            if (existing != null)
            {
                return new ResendResult(existing, false);
            }
            throw;
        }
        await TrySendAsync(resend, null, cancellationToken);
        return new ResendResult(resend, true);
    }

    public async Task<bool> DeleteContentAsync(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _db.OrderNotifications.SingleOrDefaultAsync(x => x.Id == notificationId, cancellationToken);
        if (notification == null)
        {
            return false;
        }
        if (notification.Body == null)
        {
            return true;
        }

        if (notification.ProviderMessageSid != null)
        {
            var result = await _smsProvider.RedactMessageAsync(notification.ProviderMessageSid, cancellationToken);
            notification.RefreshProviderState(result.Status, result.ErrorCode, true, DateTimeOffset.UtcNow);
        }
        notification.MarkContentDeleted(DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (from > to)
        {
            throw new OrderInputException("The from value must be earlier than or equal to to.");
        }
        if (to - from > TimeSpan.FromDays(366))
        {
            throw new OrderInputException("The reconciliation range cannot exceed 366 days.");
        }

        var providerMessages = await _smsProvider.ListMessagesAsync(from, to, cancellationToken);
        var providerSids = providerMessages.Select(x => x.Sid).ToHashSet(StringComparer.Ordinal);
        var local = await _db.OrderNotifications
            .Where(x => (x.CreatedAt >= from && x.CreatedAt <= to) ||
                        (x.ScheduledFor >= from && x.ScheduledFor <= to) ||
                        (x.ProviderMessageSid != null && providerSids.Contains(x.ProviderMessageSid)))
            .ToListAsync(cancellationToken);
        var bySid = local
            .Where(x => x.ProviderMessageSid != null)
            .ToDictionary(x => x.ProviderMessageSid!, StringComparer.Ordinal);
        var rows = new List<ReconciliationRow>();

        foreach (var provider in providerMessages)
        {
            if (bySid.TryGetValue(provider.Sid, out var app))
            {
                app.RefreshProviderState(provider.Status, provider.ErrorCode, provider.Body == string.Empty, DateTimeOffset.UtcNow);
                rows.Add(new ReconciliationRow("matched", app.Id, provider.Sid, app.ProviderStatus, provider.Status, provider.DateSent ?? provider.DateCreated));
            }
            else
            {
                rows.Add(new ReconciliationRow("provider_only", null, provider.Sid, null, provider.Status, provider.DateSent ?? provider.DateCreated));
            }
        }

        rows.AddRange(local
            .Where(x => x.ProviderMessageSid == null || !providerSids.Contains(x.ProviderMessageSid))
            .Select(x => new ReconciliationRow(
                "application_only",
                x.Id,
                x.ProviderMessageSid,
                x.ProviderStatus,
                null,
                x.ScheduledFor ?? x.CreatedAt)));
        await _db.SaveChangesAsync(cancellationToken);
        return new ReconciliationReport(from, to, rows.OrderBy(x => x.Timestamp).ToList());
    }

    private async Task NotifyAllAsync(
        Order order,
        NotificationKind kind,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        var contacts = await _db.ContactNumbers
            .Where(x => x.BuyerId == order.BuyerId)
            .ToListAsync(cancellationToken);
        foreach (var contact in contacts)
        {
            var notification = new OrderNotification(
                order.Id,
                order.BuyerId,
                contact.Id,
                contact.Value,
                kind,
                body,
                DateTimeOffset.UtcNow);
            _db.OrderNotifications.Add(notification);
            await _db.SaveChangesAsync(cancellationToken);
            await TrySendAsync(notification, sendAt, cancellationToken);
        }
    }

    private async Task TrySendAsync(OrderNotification notification, DateTimeOffset? sendAt, CancellationToken cancellationToken)
    {
        try
        {
            var message = await _smsProvider.SendAsync(notification.Destination, notification.Body!, sendAt, cancellationToken);
            notification.RecordProviderResult(message.Sid, message.Status, message.ErrorCode, sendAt, DateTimeOffset.UtcNow);
        }
        catch (SmsProviderException ex)
        {
            notification.RecordProviderFailure(ex.ProviderErrorCode, DateTimeOffset.UtcNow);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            notification.RecordProviderFailure(null, DateTimeOffset.UtcNow);
        }
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task RefreshNotificationsAsync(List<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications.Where(x => x.ProviderMessageSid != null))
        {
            try
            {
                await RefreshNotificationAsync(notification, cancellationToken);
            }
            catch (SmsProviderException)
            {
                // A read returns the last durable provider state when a refresh is temporarily unavailable.
            }
        }
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task RefreshNotificationAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        var current = await _smsProvider.GetMessageAsync(notification.ProviderMessageSid!, cancellationToken);
        notification.RefreshProviderState(current.Status, current.ErrorCode, current.Body == string.Empty, DateTimeOffset.UtcNow);
    }

    private static void ValidateAddress(AddressInput address)
    {
        if (address == null ||
            string.IsNullOrWhiteSpace(address.Street) ||
            string.IsNullOrWhiteSpace(address.City) ||
            string.IsNullOrWhiteSpace(address.Country) ||
            string.IsNullOrWhiteSpace(address.ZipCode))
        {
            throw new OrderInputException("A complete shipping address is required.");
        }
    }
}

public sealed record ContactRegistrationResult(ContactNumber? Contact, IReadOnlyList<string> ValidationErrors);
public sealed record ResendResult(OrderNotification Notification, bool WasCreated);
public sealed record OrderSummary(int OrderId, DateTimeOffset OrderDate, string Status, decimal Total, IReadOnlyDictionary<string, int> Notifications);
public sealed record ReconciliationReport(DateTimeOffset From, DateTimeOffset To, IReadOnlyList<ReconciliationRow> Messages);
public sealed record ReconciliationRow(string Match, int? NotificationId, string? ProviderMessageSid, string? ApplicationStatus, string? ProviderStatus, DateTimeOffset? Timestamp);
public sealed record OrderLineInput(int CatalogItemId, int Quantity);
public sealed record AddressInput(string Street, string City, string State, string Country, string ZipCode);

public sealed class OrderInputException : Exception
{
    public OrderInputException(string message) : base(message) { }
}

public sealed class ResendNotAllowedException : Exception
{
    public ResendNotAllowedException(string message) : base(message) { }
}

public sealed class FollowUpCancellationException : Exception
{
    public FollowUpCancellationException(string message) : base(message) { }
}
