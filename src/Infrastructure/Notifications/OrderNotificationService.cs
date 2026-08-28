using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.Infrastructure.Notifications;

public sealed class OrderNotificationService
{
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);
    private readonly CatalogContext _dbContext;
    private readonly ITwilioGateway _twilio;
    private readonly TimeProvider _timeProvider;

    public OrderNotificationService(CatalogContext dbContext, ITwilioGateway twilio, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _twilio = twilio;
        _timeProvider = timeProvider;
    }

    public async Task<ContactNumber> RegisterContactNumberAsync(string buyerId, string input, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            throw new WorkflowValidationException("A phone number is required.");
        }

        var validation = await _twilio.ValidatePhoneNumberAsync(input, cancellationToken);
        if (!validation.IsValid || string.IsNullOrWhiteSpace(validation.CanonicalNumber))
        {
            throw new WorkflowValidationException("Twilio does not consider this a valid destination.");
        }

        var duplicate = await _dbContext.ContactNumbers.AnyAsync(
            x => x.BuyerId == buyerId && x.CanonicalNumber == validation.CanonicalNumber,
            cancellationToken);
        if (duplicate)
        {
            throw new WorkflowConflictException("That contact number is already registered.");
        }

        var contactNumber = new ContactNumber(buyerId, validation.CanonicalNumber);
        _dbContext.ContactNumbers.Add(contactNumber);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return contactNumber;
    }

    public async Task<IReadOnlyList<ContactNumber>> GetContactNumbersAsync(string buyerId, CancellationToken cancellationToken) =>
        await _dbContext.ContactNumbers
            .AsNoTracking()
            .Where(x => x.BuyerId == buyerId)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);

    public async Task<bool> DeleteContactNumberAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken)
    {
        var contactNumber = await _dbContext.ContactNumbers
            .SingleOrDefaultAsync(x => x.Id == contactNumberId && x.BuyerId == buyerId, cancellationToken);
        if (contactNumber is null)
        {
            return false;
        }

        var scheduled = await _dbContext.OrderNotifications
            .Where(x => x.ContactNumberId == contactNumberId &&
                        x.ProviderMessageSid != null &&
                        (x.ProviderStatus == "scheduled" || x.ProviderStatus == "accepted" || x.ProviderStatus == "queued"))
            .ToListAsync(cancellationToken);

        // Complete provider cancellation before deleting locally. A failed delete can be
        // retried safely, while a successful delete guarantees no queued follow-up remains.
        foreach (var notification in scheduled)
        {
            var providerMessage = await _twilio.CancelMessageAsync(notification.ProviderMessageSid!, cancellationToken);
            notification.ApplyProviderState(providerMessage.Sid, providerMessage.Status, providerMessage.ErrorCode,
                providerMessage.DateCreated, providerMessage.DateSent);
        }

        _dbContext.ContactNumbers.Remove(contactNumber);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> requestedItems, CancellationToken cancellationToken)
    {
        if (requestedItems.Count == 0 || requestedItems.Any(x => x.CatalogItemId <= 0 || x.Quantity <= 0))
        {
            throw new WorkflowValidationException("At least one catalog item with a positive quantity is required.");
        }

        if (requestedItems.Select(x => x.CatalogItemId).Distinct().Count() != requestedItems.Count)
        {
            throw new WorkflowValidationException("Each catalog item may appear only once.");
        }

        var ids = requestedItems.Select(x => x.CatalogItemId).ToArray();
        var catalogItems = await _dbContext.CatalogItems
            .Where(x => ids.Contains(x.Id))
            .ToListAsync(cancellationToken);
        if (catalogItems.Count != ids.Length)
        {
            throw new WorkflowValidationException("One or more catalog items do not exist.");
        }

        var orderItems = requestedItems.Select(input =>
        {
            var catalogItem = catalogItems.Single(x => x.Id == input.CatalogItemId);
            var ordered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri);
            return new OrderItem(ordered, catalogItem.Price, input.Quantity);
        }).ToList();

        var address = new Address("Not supplied", "Not supplied", string.Empty, "Not supplied", "Not supplied");
        var order = new Order(buyerId, address, orderItems);
        _dbContext.Orders.Add(order);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await NotifyAllAsync(order, NotificationKind.OrderPlaced,
            $"eShopOnWeb: Order {order.Id} has been placed.", null, cancellationToken);
        return order;
    }

    public async Task<Order?> DispatchOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _dbContext.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        try
        {
            order.Dispatch();
        }
        catch (InvalidOperationException exception)
        {
            throw new WorkflowConflictException(exception.Message);
        }
        await _dbContext.SaveChangesAsync(cancellationToken);

        await NotifyAllAsync(order, NotificationKind.OrderDispatched,
            $"eShopOnWeb: Order {order.Id} has been dispatched and is on its way.", null, cancellationToken);

        var sendAt = _timeProvider.GetUtcNow().Add(FollowUpDelay);
        await NotifyAllAsync(order, NotificationKind.DeliveryFollowUp,
            $"eShopOnWeb: How did delivery of order {order.Id} go?", sendAt, cancellationToken);
        return order;
    }

    public async Task<Order?> CancelOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _dbContext.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        try
        {
            order.Cancel();
        }
        catch (InvalidOperationException exception)
        {
            throw new WorkflowConflictException(exception.Message);
        }
        await _dbContext.SaveChangesAsync(cancellationToken);

        var followUps = await _dbContext.OrderNotifications
            .Where(x => x.OrderId == order.Id && x.Kind == NotificationKind.DeliveryFollowUp &&
                        x.ProviderMessageSid != null && x.ProviderStatus != "canceled")
            .ToListAsync(cancellationToken);

        foreach (var followUp in followUps)
        {
            try
            {
                var cancelled = await _twilio.CancelMessageAsync(followUp.ProviderMessageSid!, cancellationToken);
                followUp.ApplyProviderState(cancelled.Sid, cancelled.Status, cancelled.ErrorCode,
                    cancelled.DateCreated, cancelled.DateSent);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // The order transition remains successful. The provider state is retained so
                // a later refresh/reconciliation makes the cancellation failure visible.
            }
        }

        await NotifyAllAsync(order, NotificationKind.OrderCancelled,
            $"eShopOnWeb: Order {order.Id} has been cancelled.", null, cancellationToken);
        return order;
    }

    public async Task<IReadOnlyList<Order>> GetOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        var orders = await _dbContext.Orders
            .AsNoTracking()
            .Include(x => x.OrderItems)
            .Where(x => x.BuyerId == buyerId)
            .OrderByDescending(x => x.OrderDate)
            .ToListAsync(cancellationToken);

        var orderIds = orders.Select(x => x.Id).ToArray();
        var notifications = await _dbContext.OrderNotifications
            .Where(x => orderIds.Contains(x.OrderId))
            .ToListAsync(cancellationToken);
        await RefreshProviderStateAsync(notifications, cancellationToken);
        return orders;
    }

    public async Task<IReadOnlyList<OrderNotification>?> GetOrderNotificationsAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var ownsOrder = await _dbContext.Orders.AnyAsync(x => x.Id == orderId && x.BuyerId == buyerId, cancellationToken);
        if (!ownsOrder)
        {
            return null;
        }

        var notifications = await _dbContext.OrderNotifications
            .Where(x => x.OrderId == orderId)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        await RefreshProviderStateAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<IReadOnlyDictionary<int, IReadOnlyList<OrderNotification>>> GetNotificationMapAsync(
        IReadOnlyCollection<int> orderIds,
        CancellationToken cancellationToken)
    {
        var notifications = await _dbContext.OrderNotifications.AsNoTracking()
            .Where(x => orderIds.Contains(x.OrderId))
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        return notifications.GroupBy(x => x.OrderId)
            .ToDictionary(x => x.Key, x => (IReadOnlyList<OrderNotification>)x.ToList());
    }

    public async Task<OrderNotification?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 200)
        {
            throw new WorkflowValidationException("An idempotency key of at most 200 characters is required.");
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(idempotencyKey)));
        var existing = await _dbContext.OrderNotifications.SingleOrDefaultAsync(
            x => x.ResendOfNotificationId == notificationId && x.IdempotencyKeyHash == hash,
            cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var original = await _dbContext.OrderNotifications.SingleOrDefaultAsync(x => x.Id == notificationId, cancellationToken);
        if (original is null)
        {
            return null;
        }

        await RefreshProviderStateAsync(new[] { original }, cancellationToken);
        if (original.ProviderStatus is not ("failed" or "undelivered"))
        {
            throw new WorkflowConflictException("Only a failed or undelivered notification can be resent.");
        }
        if (original.ContentDisposed || string.IsNullOrWhiteSpace(original.Content))
        {
            throw new WorkflowConflictException("Disposed notification content cannot be resent.");
        }
        if (!original.ContactNumberId.HasValue)
        {
            throw new WorkflowConflictException("The destination contact number has been removed.");
        }

        var contact = await _dbContext.ContactNumbers.SingleOrDefaultAsync(x => x.Id == original.ContactNumberId.Value, cancellationToken);
        if (contact is null)
        {
            throw new WorkflowConflictException("The destination contact number has been removed.");
        }

        var orderStatus = await _dbContext.Orders.Where(x => x.Id == original.OrderId).Select(x => x.Status).SingleAsync(cancellationToken);
        if (original.Kind == NotificationKind.DeliveryFollowUp && orderStatus == OrderStatus.Cancelled)
        {
            throw new WorkflowConflictException("A delivery follow-up for a cancelled order cannot be resent.");
        }

        var resend = new OrderNotification(original.OrderId, contact.Id, NotificationKind.Resend,
            original.Content, null, original.Id, hash);
        _dbContext.OrderNotifications.Add(resend);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _dbContext.Entry(resend).State = EntityState.Detached;
            var racedRequest = await _dbContext.OrderNotifications.SingleOrDefaultAsync(
                x => x.ResendOfNotificationId == notificationId && x.IdempotencyKeyHash == hash,
                cancellationToken);
            if (racedRequest is not null)
            {
                return racedRequest;
            }
            throw;
        }
        await SendNotificationAsync(resend, contact.CanonicalNumber, cancellationToken);
        return resend;
    }

    public async Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _dbContext.OrderNotifications.SingleOrDefaultAsync(x => x.Id == notificationId, cancellationToken);
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
            var current = await _twilio.GetMessageAsync(notification.ProviderMessageSid, cancellationToken);
            if (current.Status == "scheduled")
            {
                current = await _twilio.CancelMessageAsync(notification.ProviderMessageSid, cancellationToken);
            }
            var redacted = await _twilio.RedactMessageAsync(notification.ProviderMessageSid, cancellationToken);
            notification.ApplyProviderState(redacted.Sid, current.Status, redacted.ErrorCode,
                redacted.DateCreated, redacted.DateSent);
        }

        notification.DisposeContent();
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<ReconciliationResult> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        if (from > to)
        {
            throw new WorkflowValidationException("The from date must not be after the to date.");
        }
        if (from <= DateTimeOffset.MinValue.AddDays(1) || to >= DateTimeOffset.MaxValue.AddDays(-1))
        {
            throw new WorkflowValidationException("The reconciliation range is outside the provider's supported date range.");
        }

        var local = await _dbContext.OrderNotifications.AsNoTracking()
            .Where(x => x.CreatedAt >= from && x.CreatedAt <= to)
            .ToListAsync(cancellationToken);
        var providerMessages = (await _twilio.ListMessagesAsync(from, to, cancellationToken)).ToList();

        // DateSent filtering cannot return scheduled/canceled resources because they have
        // no sent timestamp. Fetch known provider SIDs so those records are still compared.
        var listedSids = providerMessages.Select(x => x.Sid).ToHashSet(StringComparer.Ordinal);
        foreach (var notification in local.Where(x => x.ProviderMessageSid is not null && !listedSids.Contains(x.ProviderMessageSid)))
        {
            try
            {
                var providerMessage = await _twilio.GetMessageAsync(notification.ProviderMessageSid!, cancellationToken);
                var timestamp = providerMessage.DateSent ?? providerMessage.DateCreated;
                if (timestamp >= from && timestamp <= to)
                {
                    providerMessages.Add(providerMessage);
                    listedSids.Add(providerMessage.Sid);
                }
            }
            catch (TwilioProviderException)
            {
                // The row below remains eShop-only, which is exactly the discrepancy the
                // report must retain when a provider resource cannot be found.
            }
        }

        var localBySid = local.Where(x => x.ProviderMessageSid != null).ToDictionary(x => x.ProviderMessageSid!);
        var providerSids = providerMessages.Select(x => x.Sid).ToHashSet(StringComparer.Ordinal);

        var rows = providerMessages.Select(provider =>
        {
            localBySid.TryGetValue(provider.Sid, out var match);
            return new ReconciliationRow(provider.Sid, match?.Id, provider.Status, match?.ProviderStatus,
                provider.DateSent ?? provider.DateCreated, match is null ? "provider-only" : "matched");
        }).ToList();

        rows.AddRange(local
            .Where(x => x.ProviderMessageSid is null || !providerSids.Contains(x.ProviderMessageSid))
            .Select(x => new ReconciliationRow(x.ProviderMessageSid, x.Id, null, x.ProviderStatus,
                x.ProviderSentAt ?? x.ProviderCreatedAt ?? x.CreatedAt, "eshop-only")));

        return new ReconciliationResult(from, to, rows.OrderBy(x => x.Timestamp).ToList());
    }

    private async Task NotifyAllAsync(Order order, NotificationKind kind, string content, DateTimeOffset? scheduledFor, CancellationToken cancellationToken)
    {
        var contacts = await _dbContext.ContactNumbers.Where(x => x.BuyerId == order.BuyerId).ToListAsync(cancellationToken);
        foreach (var contact in contacts)
        {
            var notification = new OrderNotification(order.Id, contact.Id, kind, content, scheduledFor);
            _dbContext.OrderNotifications.Add(notification);
            await _dbContext.SaveChangesAsync(cancellationToken);

            if (scheduledFor.HasValue)
            {
                await ScheduleNotificationAsync(notification, contact.CanonicalNumber, scheduledFor.Value, cancellationToken);
            }
            else
            {
                await SendNotificationAsync(notification, contact.CanonicalNumber, cancellationToken);
            }
        }
    }

    private async Task SendNotificationAsync(OrderNotification notification, string destination, CancellationToken cancellationToken)
    {
        try
        {
            var sent = await _twilio.SendMessageAsync(destination, notification.Content!, cancellationToken);
            notification.ApplyProviderState(sent.Sid, sent.Status, sent.ErrorCode, sent.DateCreated, sent.DateSent);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            notification.MarkProviderFailure((exception as TwilioProviderException)?.ProviderErrorCode);
        }
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task ScheduleNotificationAsync(OrderNotification notification, string destination, DateTimeOffset sendAt, CancellationToken cancellationToken)
    {
        try
        {
            var scheduled = await _twilio.ScheduleMessageAsync(destination, notification.Content!, sendAt, cancellationToken);
            notification.ApplyProviderState(scheduled.Sid, scheduled.Status, scheduled.ErrorCode,
                scheduled.DateCreated, scheduled.DateSent);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            notification.MarkProviderFailure((exception as TwilioProviderException)?.ProviderErrorCode);
        }
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task RefreshProviderStateAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        var changed = false;
        foreach (var notification in notifications.Where(x => x.ProviderMessageSid is not null))
        {
            try
            {
                var provider = await _twilio.GetMessageAsync(notification.ProviderMessageSid!, cancellationToken);
                notification.ApplyProviderState(provider.Sid, provider.Status, provider.ErrorCode,
                    provider.DateCreated, provider.DateSent);
                changed = true;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // A read failure must not make the shopper-facing read endpoint fail.
            }
        }
        if (changed)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}

public sealed record OrderLineInput(int CatalogItemId, int Quantity);
public sealed record ReconciliationResult(DateTimeOffset From, DateTimeOffset To, IReadOnlyList<ReconciliationRow> Messages);
public sealed record ReconciliationRow(string? ProviderMessageSid, int? NotificationId, string? ProviderStatus,
    string? EshopStatus, DateTimeOffset? Timestamp, string Match);

public sealed class WorkflowValidationException : Exception
{
    public WorkflowValidationException(string message) : base(message) { }
}

public sealed class WorkflowConflictException : Exception
{
    public WorkflowConflictException(string message) : base(message) { }
}
