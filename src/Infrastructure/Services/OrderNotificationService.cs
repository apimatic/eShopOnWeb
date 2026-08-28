using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public sealed class OrderNotificationService : IOrderNotificationService
{
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);
    private readonly CatalogContext _db;
    private readonly IPhoneNumberValidator _phoneNumberValidator;
    private readonly ITextMessagingProvider _messagingProvider;

    public OrderNotificationService(CatalogContext db, IPhoneNumberValidator phoneNumberValidator,
        ITextMessagingProvider messagingProvider)
    {
        _db = db;
        _phoneNumberValidator = phoneNumberValidator;
        _messagingProvider = messagingProvider;
    }

    public async Task<ContactNumber> RegisterContactNumberAsync(string buyerId, string input,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            throw new OrderNotificationValidationException("A mobile number is required.");
        }

        var result = await _phoneNumberValidator.ValidateAsync(input, cancellationToken);
        if (!result.IsValid || string.IsNullOrWhiteSpace(result.CanonicalNumber))
        {
            throw new OrderNotificationValidationException("Twilio does not consider this a usable destination.");
        }

        var existing = await _db.ContactNumbers.SingleOrDefaultAsync(
            x => x.BuyerId == buyerId && x.CanonicalNumber == result.CanonicalNumber, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var contact = new ContactNumber(buyerId, result.CanonicalNumber);
        _db.ContactNumbers.Add(contact);
        await _db.SaveChangesAsync(cancellationToken);
        return contact;
    }

    public async Task<IReadOnlyList<ContactNumber>> GetContactNumbersAsync(string buyerId,
        CancellationToken cancellationToken) =>
        await _db.ContactNumbers.AsNoTracking().Where(x => x.BuyerId == buyerId)
            .OrderBy(x => x.Id).ToListAsync(cancellationToken);

    public async Task<bool> DeleteContactNumberAsync(string buyerId, int contactNumberId,
        CancellationToken cancellationToken)
    {
        var contact = await _db.ContactNumbers.SingleOrDefaultAsync(
            x => x.Id == contactNumberId && x.BuyerId == buyerId, cancellationToken);
        if (contact is null)
        {
            return false;
        }

        var scheduled = await _db.OrderNotifications.Where(x =>
                x.ContactNumberId == contactNumberId && x.Kind == NotificationKind.DeliveryFollowUp &&
                x.ProviderMessageSid != null &&
                (x.DeliveryStatus == NotificationDeliveryStatus.Scheduled ||
                 x.DeliveryStatus == NotificationDeliveryStatus.InProgress ||
                 x.DeliveryStatus == NotificationDeliveryStatus.ProviderRequestFailed ||
                 x.DeliveryStatus == NotificationDeliveryStatus.Unknown))
            .ToListAsync(cancellationToken);

        // Only remove the destination after the provider confirms every queued follow-up is cancelled.
        foreach (var notification in scheduled)
        {
            var cancelled = await _messagingProvider.CancelAsync(notification.ProviderMessageSid!, cancellationToken);
            notification.ApplyProviderState(cancelled);
        }

        _db.ContactNumbers.Remove(contact);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> lines,
        CancellationToken cancellationToken)
    {
        var normalized = NormalizeLines(lines);
        var itemIds = normalized.Select(x => x.CatalogItemId).ToArray();
        var catalogItems = await _db.CatalogItems.Where(x => itemIds.Contains(x.Id)).ToListAsync(cancellationToken);
        if (catalogItems.Count != itemIds.Length)
        {
            var missing = itemIds.Except(catalogItems.Select(x => x.Id));
            throw new OrderNotificationValidationException($"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var quantities = normalized.ToDictionary(x => x.CatalogItemId, x => x.Quantity);
        var orderItems = catalogItems.Select(x => new OrderItem(
            new CatalogItemOrdered(x.Id, x.Name, x.PictureUri), x.Price, quantities[x.Id])).ToList();
        var address = new Address("Not supplied", "Not supplied", string.Empty, "Not supplied", "N/A");
        var order = new Order(buyerId, address, orderItems);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);

        await NotifyAllContactsAsync(order, NotificationKind.OrderPlaced,
            $"Your eShop order #{order.Id} was placed successfully.", null, cancellationToken);
        return order;
    }

    public async Task<Order?> DispatchOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        try
        {
            order.Dispatch();
        }
        catch (InvalidOperationException ex)
        {
            throw new OrderNotificationConflictException(ex.Message);
        }
        await _db.SaveChangesAsync(cancellationToken);

        await NotifyAllContactsAsync(order, NotificationKind.OrderDispatched,
            $"Your eShop order #{order.Id} is on its way.", null, cancellationToken);
        var scheduledFor = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        await NotifyAllContactsAsync(order, NotificationKind.DeliveryFollowUp,
            $"How did delivery of your eShop order #{order.Id} go?", scheduledFor, cancellationToken);
        return order;
    }

    public async Task<Order?> CancelOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        try
        {
            order.Cancel();
        }
        catch (InvalidOperationException ex)
        {
            throw new OrderNotificationConflictException(ex.Message);
        }
        await _db.SaveChangesAsync(cancellationToken);

        var followUps = await _db.OrderNotifications.Where(x => x.OrderId == orderId &&
                x.Kind == NotificationKind.DeliveryFollowUp && x.ProviderMessageSid != null &&
                (x.DeliveryStatus == NotificationDeliveryStatus.Scheduled ||
                 x.DeliveryStatus == NotificationDeliveryStatus.InProgress ||
                 x.DeliveryStatus == NotificationDeliveryStatus.ProviderRequestFailed ||
                 x.DeliveryStatus == NotificationDeliveryStatus.Unknown))
            .ToListAsync(cancellationToken);
        foreach (var followUp in followUps)
        {
            try
            {
                var cancelled = await _messagingProvider.CancelAsync(followUp.ProviderMessageSid!, cancellationToken);
                followUp.ApplyProviderState(cancelled);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                followUp.RecordProviderFailure(SafeProviderFailure(ex));
            }
        }
        await _db.SaveChangesAsync(cancellationToken);

        await NotifyAllContactsAsync(order, NotificationKind.OrderCancelled,
            $"Your eShop order #{order.Id} was cancelled.", null, cancellationToken);
        return order;
    }

    public async Task<IReadOnlyList<OrderSummary>> GetOrdersAsync(string buyerId,
        CancellationToken cancellationToken)
    {
        var orders = await _db.Orders.AsNoTracking().Include(x => x.OrderItems)
            .Where(x => x.BuyerId == buyerId).OrderByDescending(x => x.OrderDate).ToListAsync(cancellationToken);
        var ids = orders.Select(x => x.Id).ToArray();
        var notifications = await _db.OrderNotifications.AsNoTracking().Where(x => ids.Contains(x.OrderId))
            .ToListAsync(cancellationToken);
        return orders.Select(order => new OrderSummary(order.Id, order.OrderDate, order.Status.ToString(), order.Total(),
            notifications.Where(x => x.OrderId == order.Id)
                .GroupBy(x => new { x.Kind, x.DeliveryStatus, x.ProviderStatus })
                .Select(x => new NotificationProgress(x.Key.Kind.ToString(), x.Key.DeliveryStatus.ToString(),
                    x.Key.ProviderStatus, x.Count())).ToList())).ToList();
    }

    public async Task<IReadOnlyList<OrderNotification>?> GetNotificationsAsync(string buyerId, int orderId,
        CancellationToken cancellationToken)
    {
        if (!await _db.Orders.AnyAsync(x => x.Id == orderId && x.BuyerId == buyerId, cancellationToken))
        {
            return null;
        }

        var notifications = await _db.OrderNotifications.Where(x => x.OrderId == orderId)
            .OrderBy(x => x.Id).ToListAsync(cancellationToken);
        foreach (var notification in notifications.Where(x => x.ProviderMessageSid is not null))
        {
            try
            {
                var current = await _messagingProvider.FetchAsync(notification.ProviderMessageSid!, cancellationToken);
                notification.ApplyProviderState(current);
            }
            catch (Exception ex) when (IsProviderFailure(ex))
            {
                // Preserve the last known delivery outcome when a status refresh is unavailable.
            }
        }
        await _db.SaveChangesAsync(cancellationToken);
        return notifications;
    }

    public async Task<OrderNotification?> ResendAsync(int notificationId, string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 200)
        {
            throw new OrderNotificationValidationException("An idempotency key of 1 to 200 characters is required.");
        }

        var repeated = await _db.OrderNotifications.SingleOrDefaultAsync(x =>
            x.SourceNotificationId == notificationId && x.IdempotencyKey == idempotencyKey, cancellationToken);
        if (repeated is not null)
        {
            return repeated;
        }

        var source = await _db.OrderNotifications.SingleOrDefaultAsync(x => x.Id == notificationId, cancellationToken);
        if (source is null)
        {
            return null;
        }
        if (source.ContentDisposed || string.IsNullOrEmpty(source.Content))
        {
            throw new OrderNotificationConflictException("Disposed notification content cannot be resent.");
        }
        var order = await _db.Orders.AsNoTracking().SingleAsync(x => x.Id == source.OrderId, cancellationToken);
        if (order.Status == OrderStatus.Cancelled && source.Kind == NotificationKind.DeliveryFollowUp)
        {
            throw new OrderNotificationConflictException("A delivery follow-up for a cancelled order cannot be resent.");
        }
        if (source.ProviderMessageSid is not null)
        {
            try
            {
                source.ApplyProviderState(await _messagingProvider.FetchAsync(source.ProviderMessageSid, cancellationToken));
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex) when (IsProviderFailure(ex)) { }
        }
        if (source.DeliveryStatus is not (NotificationDeliveryStatus.Failed or
            NotificationDeliveryStatus.Undelivered or NotificationDeliveryStatus.ProviderRequestFailed))
        {
            throw new OrderNotificationConflictException("Only a message that failed to reach the shopper can be resent.");
        }

        var contact = await _db.ContactNumbers.SingleOrDefaultAsync(x => x.Id == source.ContactNumberId,
            cancellationToken);
        if (contact is null)
        {
            throw new OrderNotificationConflictException("The destination was removed and cannot be messaged again.");
        }

        var resend = new OrderNotification(source.OrderId, source.ContactNumberId, source.Kind,
            source.Content, source.Id, idempotencyKey);
        _db.OrderNotifications.Add(resend);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _db.Entry(resend).State = EntityState.Detached;
            var winner = await _db.OrderNotifications.AsNoTracking().SingleOrDefaultAsync(x =>
                x.SourceNotificationId == notificationId && x.IdempotencyKey == idempotencyKey, cancellationToken);
            if (winner is not null)
            {
                return winner;
            }
            throw;
        }
        await TrySendAsync(resend, contact.CanonicalNumber, null, cancellationToken);
        return resend;
    }

    public async Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _db.OrderNotifications.SingleOrDefaultAsync(x => x.Id == notificationId,
            cancellationToken);
        if (notification is null)
        {
            return false;
        }
        if (notification.ContentDisposed)
        {
            return true;
        }
        if (notification.ProviderMessageSid is not null)
        {
            var redacted = await _messagingProvider.RedactAsync(notification.ProviderMessageSid, cancellationToken);
            notification.ApplyProviderState(redacted);
        }
        notification.DisposeContent();
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<ReconciliationEntry>> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (to < from)
        {
            throw new OrderNotificationValidationException("'to' must be at or after 'from'.");
        }
        var provider = await _messagingProvider.ListAsync(from, to, cancellationToken);
        var local = await _db.OrderNotifications.AsNoTracking()
            .Where(x => x.CreatedAt >= from && x.CreatedAt <= to).ToListAsync(cancellationToken);
        var providerBySid = provider.ToDictionary(x => x.Sid, StringComparer.Ordinal);
        var localBySid = local.Where(x => x.ProviderMessageSid != null)
            .ToDictionary(x => x.ProviderMessageSid!, StringComparer.Ordinal);
        var allSids = providerBySid.Keys.Union(localBySid.Keys, StringComparer.Ordinal).OrderBy(x => x);
        var result = new List<ReconciliationEntry>();
        foreach (var sid in allSids)
        {
            providerBySid.TryGetValue(sid, out var providerMessage);
            localBySid.TryGetValue(sid, out var notification);
            result.Add(new ReconciliationEntry(sid,
                providerMessage is null ? "LocalOnly" : notification is null ? "ProviderOnly" : "Matched",
                notification?.Id, providerMessage?.Status, notification?.ProviderStatus,
                providerMessage?.DateSent ?? providerMessage?.DateCreated, providerMessage?.To,
                notification?.ContentDisposed == true ? null : providerMessage?.Body));
        }
        result.AddRange(local.Where(x => x.ProviderMessageSid is null).Select(x =>
            new ReconciliationEntry($"local:{x.Id}", "LocalOnly", x.Id, null, x.ProviderStatus,
                x.CreatedAt, null, x.ContentDisposed ? null : x.Content)));
        return result;
    }

    private async Task NotifyAllContactsAsync(Order order, NotificationKind kind, string body,
        DateTimeOffset? scheduledFor, CancellationToken cancellationToken)
    {
        var contacts = await _db.ContactNumbers.Where(x => x.BuyerId == order.BuyerId)
            .ToListAsync(cancellationToken);
        foreach (var contact in contacts)
        {
            var notification = new OrderNotification(order.Id, contact.Id, kind, body);
            _db.OrderNotifications.Add(notification);
            await _db.SaveChangesAsync(cancellationToken);
            await TrySendAsync(notification, contact.CanonicalNumber, scheduledFor, cancellationToken);
        }
    }

    private async Task TrySendAsync(OrderNotification notification, string canonicalNumber,
        DateTimeOffset? scheduledFor, CancellationToken cancellationToken)
    {
        try
        {
            var message = await _messagingProvider.SendAsync(canonicalNumber, notification.Content!, scheduledFor,
                cancellationToken);
            notification.RecordProviderMessage(message, scheduledFor);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            notification.RecordProviderFailure(SafeProviderFailure(ex));
        }
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static List<OrderLineInput> NormalizeLines(IReadOnlyList<OrderLineInput> lines)
    {
        if (lines is null || lines.Count == 0)
        {
            throw new OrderNotificationValidationException("At least one catalog item is required.");
        }
        if (lines.Any(x => x.CatalogItemId <= 0 || x.Quantity <= 0))
        {
            throw new OrderNotificationValidationException("Catalog item ids and quantities must be positive.");
        }
        return lines.GroupBy(x => x.CatalogItemId)
            .Select(x => new OrderLineInput(x.Key, checked(x.Sum(y => y.Quantity)))).ToList();
    }

    private static bool IsProviderFailure(Exception exception) =>
        exception is MessagingProviderException or HttpRequestException or TaskCanceledException;

    private static string SafeProviderFailure(Exception exception) => exception is MessagingProviderException provider &&
        provider.ProviderCode.HasValue ? $"Provider request failed (code {provider.ProviderCode.Value})." :
        "Provider request failed.";
}
