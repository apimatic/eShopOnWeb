using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Notifications;

public sealed class OrderNotificationService : IOrderNotificationService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ResendLocks = new();
    private static readonly string[] RetryableStatuses = { "failed", "undelivered", "send_failed", "partially_delivered" };
    private readonly CatalogContext _db;
    private readonly IPhoneNumberValidator _phoneValidator;
    private readonly IMessageProvider _messageProvider;
    private readonly ILogger<OrderNotificationService> _logger;
    private readonly TimeProvider _timeProvider;

    public OrderNotificationService(CatalogContext db, IPhoneNumberValidator phoneValidator,
        IMessageProvider messageProvider, ILogger<OrderNotificationService> logger, TimeProvider timeProvider)
    {
        _db = db;
        _phoneValidator = phoneValidator;
        _messageProvider = messageProvider;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public async Task<ContactNumberResult> RegisterContactNumberAsync(string buyerId, string phoneNumber,
        string? countryCode, CancellationToken cancellationToken = default)
    {
        RequireBuyer(buyerId);
        var validation = await _phoneValidator.ValidateAsync(phoneNumber, countryCode, cancellationToken);
        if (!validation.IsValid || string.IsNullOrWhiteSpace(validation.CanonicalNumber))
            throw new NotificationValidationException(validation.Reason ?? "The destination is not valid.");

        var exists = await _db.ContactNumbers.AnyAsync(x => x.BuyerId == buyerId &&
            x.CanonicalNumber == validation.CanonicalNumber, cancellationToken);
        if (exists) throw new NotificationConflictException("That contact number is already registered.");

        var contact = new ContactNumber(buyerId, validation.CanonicalNumber, Now);
        _db.ContactNumbers.Add(contact);
        await _db.SaveChangesAsync(cancellationToken);
        return Map(contact);
    }

    public async Task<IReadOnlyList<ContactNumberResult>> GetContactNumbersAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        RequireBuyer(buyerId);
        return await _db.ContactNumbers.AsNoTracking().Where(x => x.BuyerId == buyerId)
            .OrderBy(x => x.Id).Select(x => new ContactNumberResult(x.Id, x.CanonicalNumber, x.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> DeleteContactNumberAsync(string buyerId, int contactNumberId,
        CancellationToken cancellationToken = default)
    {
        RequireBuyer(buyerId);
        var contact = await _db.ContactNumbers.FirstOrDefaultAsync(
            x => x.Id == contactNumberId && x.BuyerId == buyerId, cancellationToken);
        if (contact is null) return false;
        _db.ContactNumbers.Remove(contact);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<OrderResult> PlaceOrderAsync(string buyerId, Address shipToAddress,
        IReadOnlyList<OrderLineRequest> items, CancellationToken cancellationToken = default)
    {
        RequireBuyer(buyerId);
        if (items.Count == 0) throw new NotificationValidationException("At least one catalog item is required.");
        if (items.Any(x => x.CatalogItemId <= 0 || x.Quantity <= 0))
            throw new NotificationValidationException("Catalog item ids and quantities must be positive.");

        var combined = items.GroupBy(x => x.CatalogItemId)
            .Select(x => new OrderLineRequest(x.Key, x.Sum(y => y.Quantity))).ToList();
        var ids = combined.Select(x => x.CatalogItemId).ToList();
        var catalogItems = await _db.CatalogItems.Where(x => ids.Contains(x.Id)).ToListAsync(cancellationToken);
        var missing = ids.Except(catalogItems.Select(x => x.Id)).ToList();
        if (missing.Count != 0)
            throw new NotificationValidationException($"Catalog item {missing[0]} does not exist.");

        var orderItems = combined.Select(line =>
        {
            var catalogItem = catalogItems.Single(x => x.Id == line.CatalogItemId);
            return new OrderItem(new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri),
                catalogItem.Price, line.Quantity);
        }).ToList();
        var order = new Order(buyerId, shipToAddress, orderItems);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);

        await NotifyAllContactsAsync(order, NotificationKind.OrderPlaced,
            $"Your eShop order #{order.Id} has been placed.", null);
        return await MapOrderAsync(order, cancellationToken);
    }

    public async Task<OrderResult?> DispatchOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await FindOrderAsync(orderId, cancellationToken);
        if (order is null) return null;
        if (order.Status == OrderStatus.Cancelled)
            throw new NotificationConflictException("A cancelled order cannot be dispatched.");

        if (order.Dispatch(Now))
        {
            await _db.SaveChangesAsync(cancellationToken);
            await NotifyAllContactsAsync(order, NotificationKind.OrderDispatched,
                $"Your eShop order #{order.Id} has been dispatched and is on its way.", null);
            var followUpAt = Now.AddDays(3);
            await NotifyAllContactsAsync(order, NotificationKind.DeliveryFollowUp,
                $"How did delivery of your eShop order #{order.Id} go?", followUpAt);
        }

        return await MapOrderAsync(order, cancellationToken);
    }

    public async Task<OrderResult?> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await FindOrderAsync(orderId, cancellationToken);
        if (order is null) return null;

        if (order.Cancel(Now))
        {
            var followUps = await _db.OrderNotifications.Where(x => x.OrderId == orderId &&
                x.Kind == NotificationKind.DeliveryFollowUp).ToListAsync(cancellationToken);
            foreach (var followUp in followUps) followUp.RequestCancellation();
            await _db.SaveChangesAsync(cancellationToken);

            foreach (var followUp in followUps)
                await TryCancelAsync(followUp, CancellationToken.None);

            await NotifyAllContactsAsync(order, NotificationKind.OrderCancelled,
                $"Your eShop order #{order.Id} has been cancelled.", null);
        }

        return await MapOrderAsync(order, cancellationToken);
    }

    public async Task<IReadOnlyList<OrderResult>> GetOrdersAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        RequireBuyer(buyerId);
        var orders = await _db.Orders.Include(x => x.OrderItems).Where(x => x.BuyerId == buyerId)
            .OrderByDescending(x => x.OrderDate).ToListAsync(cancellationToken);
        var results = new List<OrderResult>(orders.Count);
        foreach (var order in orders) results.Add(await MapOrderAsync(order, cancellationToken));
        return results;
    }

    public async Task<IReadOnlyList<NotificationResult>?> GetNotificationsAsync(string buyerId, int orderId,
        CancellationToken cancellationToken = default)
    {
        RequireBuyer(buyerId);
        var ownsOrder = await _db.Orders.AnyAsync(x => x.Id == orderId && x.BuyerId == buyerId, cancellationToken);
        if (!ownsOrder) return null;
        var notifications = await LoadAndRefreshNotificationsAsync(orderId, cancellationToken);
        return notifications.Select(Map).ToList();
    }

    public async Task<NotificationResult?> ResendAsync(int notificationId, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 200)
            throw new NotificationValidationException("An idempotency key of at most 200 characters is required.");

        var lockKey = $"{notificationId}:{idempotencyKey}";
        var gate = ResendLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var prior = await _db.NotificationResendRequests.AsNoTracking().FirstOrDefaultAsync(
                x => x.SourceNotificationId == notificationId && x.IdempotencyKey == idempotencyKey,
                cancellationToken);
            if (prior is not null)
            {
                var priorNotification = await _db.OrderNotifications.AsNoTracking()
                    .FirstAsync(x => x.Id == prior.NotificationId, cancellationToken);
                return Map(priorNotification);
            }

            var source = await _db.OrderNotifications.FirstOrDefaultAsync(x => x.Id == notificationId,
                cancellationToken);
            if (source is null) return null;
            await RefreshAsync(source, cancellationToken);
            if (!RetryableStatuses.Contains(source.ProviderStatus, StringComparer.OrdinalIgnoreCase))
                throw new NotificationConflictException("Only a message that did not reach the shopper can be resent.");
            if (source.Body is null)
                throw new NotificationConflictException("Disposed message content cannot be resent.");
            if (!source.ContactNumberId.HasValue)
                throw new NotificationConflictException("The destination has been removed and must not be messaged again.");

            var contact = await _db.ContactNumbers.AsNoTracking().FirstOrDefaultAsync(
                x => x.Id == source.ContactNumberId.Value, cancellationToken);
            if (contact is null)
                throw new NotificationConflictException("The destination has been removed and must not be messaged again.");

            var replacement = new OrderNotification(source.OrderId, contact.Id, NotificationKind.Resend,
                source.Body, Now, sourceNotificationId: source.Id);
            _db.OrderNotifications.Add(replacement);
            await _db.SaveChangesAsync(cancellationToken);
            _db.NotificationResendRequests.Add(new NotificationResendRequest(source.Id, idempotencyKey,
                replacement.Id, Now));
            await _db.SaveChangesAsync(cancellationToken);

            await TrySendAsync(replacement, contact.CanonicalNumber, CancellationToken.None);
            return Map(replacement);
        }
        finally
        {
            gate.Release();
            ResendLocks.TryRemove(lockKey, out _);
        }
    }

    public async Task<bool?> DisposeContentAsync(int notificationId,
        CancellationToken cancellationToken = default)
    {
        var notification = await _db.OrderNotifications.FirstOrDefaultAsync(x => x.Id == notificationId,
            cancellationToken);
        if (notification is null) return null;
        if (notification.ContentDisposedAt.HasValue) return true;

        if (!string.IsNullOrWhiteSpace(notification.ProviderMessageId))
        {
            ProviderMessage? state = null;
            for (var attempt = 1; attempt <= 4; attempt++)
            {
                try
                {
                    state = await _messageProvider.RedactAsync(notification.ProviderMessageId, cancellationToken);
                    break;
                }
                catch (NotificationProviderException) when (attempt < 4)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt - 1)), cancellationToken);
                }
            }
            if (state is null) throw new NotificationProviderException("Twilio content redaction did not complete.");
            notification.RecordProviderState(state.Status, state.ErrorCode, state.ErrorMessage, state.SentAt);
        }

        notification.MarkContentDisposed(Now);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<ReconciliationResult> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (from >= to) throw new NotificationValidationException("'from' must be earlier than 'to'.");
        var providerMessages = await _messageProvider.ListAsync(from, to, cancellationToken);
        var local = await _db.OrderNotifications.AsNoTracking()
            .Where(x => x.CreatedAt >= from && x.CreatedAt <= to)
            .ToListAsync(cancellationToken);
        var localIds = local.Select(x => x.Id).ToHashSet();
        foreach (var providerIdChunk in providerMessages.Select(x => x.Id).Distinct().Chunk(500))
        {
            var ids = providerIdChunk.ToList();
            var matches = await _db.OrderNotifications.AsNoTracking()
                .Where(x => x.ProviderMessageId != null && ids.Contains(x.ProviderMessageId))
                .ToListAsync(cancellationToken);
            foreach (var match in matches)
                if (localIds.Add(match.Id)) local.Add(match);
        }
        var localByProviderId = local.Where(x => x.ProviderMessageId != null)
            .ToDictionary(x => x.ProviderMessageId!, StringComparer.Ordinal);
        var entries = new List<ReconciliationEntry>();

        foreach (var provider in providerMessages)
        {
            localByProviderId.TryGetValue(provider.Id, out var notification);
            entries.Add(new(provider.Id, notification is null ? "provider-only" : "matched",
                provider.Status, notification?.Id, notification?.OrderId, provider.CreatedAt, provider.SentAt));
        }

        var providerIds = providerMessages.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var notification in local.Where(x => x.CreatedAt >= from && x.CreatedAt <= to &&
                                                       (x.ProviderMessageId is null || !providerIds.Contains(x.ProviderMessageId))))
        {
            entries.Add(new(notification.ProviderMessageId ?? $"local:{notification.Id}", "application-only",
                notification.ProviderStatus, notification.Id, notification.OrderId,
                notification.CreatedAt, notification.ProviderDateSent));
        }

        return new(from, to, entries.OrderBy(x => x.ProviderCreatedAt).ToList());
    }

    public async Task ProcessPendingCancellationsAsync(CancellationToken cancellationToken)
    {
        var pending = await _db.OrderNotifications.Where(x => x.CancellationRequested &&
            x.ProviderMessageId != null && x.ProviderStatus != "canceled").ToListAsync(cancellationToken);
        foreach (var notification in pending) await TryCancelAsync(notification, cancellationToken);
    }

    private async Task NotifyAllContactsAsync(Order order, NotificationKind kind, string body,
        DateTimeOffset? scheduledFor)
    {
        var contacts = await _db.ContactNumbers.AsNoTracking().Where(x => x.BuyerId == order.BuyerId)
            .OrderBy(x => x.Id).ToListAsync();
        foreach (var contact in contacts)
        {
            var notification = new OrderNotification(order.Id, contact.Id, kind, body, Now, scheduledFor);
            _db.OrderNotifications.Add(notification);
            await _db.SaveChangesAsync();
            await TrySendAsync(notification, contact.CanonicalNumber, CancellationToken.None);
        }
    }

    private async Task TrySendAsync(OrderNotification notification, string destination,
        CancellationToken cancellationToken)
    {
        try
        {
            var state = await _messageProvider.SendAsync(destination, notification.Body!,
                notification.ScheduledFor, cancellationToken);
            notification.RecordProviderState(state.Id, state.Status, state.ErrorCode,
                state.ErrorMessage, state.SentAt);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            notification.RecordSendFailure("The provider did not accept the send request.");
            _logger.LogWarning("Provider send failed for notification {NotificationId} on order {OrderId}.",
                notification.Id, notification.OrderId);
        }

        await _db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task TryCancelAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(notification.ProviderMessageId) ||
            string.Equals(notification.ProviderStatus, "canceled", StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            var state = await _messageProvider.CancelAsync(notification.ProviderMessageId, cancellationToken);
            notification.RecordProviderState(state.Status, state.ErrorCode, state.ErrorMessage, state.SentAt);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Provider cancellation is pending for notification {NotificationId} on order {OrderId}.",
                notification.Id, notification.OrderId);
        }
        await _db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<List<OrderNotification>> LoadAndRefreshNotificationsAsync(int orderId,
        CancellationToken cancellationToken)
    {
        var notifications = await _db.OrderNotifications.Where(x => x.OrderId == orderId)
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).ToListAsync(cancellationToken);
        foreach (var notification in notifications) await RefreshAsync(notification, cancellationToken);
        return notifications;
    }

    private async Task RefreshAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(notification.ProviderMessageId)) return;
        try
        {
            var state = notification.CancellationRequested &&
                        !string.Equals(notification.ProviderStatus, "canceled", StringComparison.OrdinalIgnoreCase)
                ? await _messageProvider.CancelAsync(notification.ProviderMessageId, cancellationToken)
                : await _messageProvider.FetchAsync(notification.ProviderMessageId, cancellationToken);
            notification.RecordProviderState(state.Status, state.ErrorCode, state.ErrorMessage, state.SentAt);
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (NotificationProviderException)
        {
            _logger.LogWarning("Provider state refresh failed for notification {NotificationId} on order {OrderId}.",
                notification.Id, notification.OrderId);
        }
    }

    private Task<Order?> FindOrderAsync(int orderId, CancellationToken cancellationToken) =>
        _db.Orders.Include(x => x.OrderItems).FirstOrDefaultAsync(x => x.Id == orderId, cancellationToken);

    private async Task<OrderResult> MapOrderAsync(Order order, CancellationToken cancellationToken)
    {
        var notifications = await LoadAndRefreshNotificationsAsync(order.Id, cancellationToken);
        return new(order.Id, order.Status.ToString().ToLowerInvariant(), order.OrderDate, order.Total(),
            order.OrderItems.Select(x => new OrderItemResult(x.ItemOrdered.CatalogItemId,
                x.ItemOrdered.ProductName, x.UnitPrice, x.Units)).ToList(),
            notifications.Select(Map).ToList());
    }

    private static NotificationResult Map(OrderNotification x) => new(x.Id, x.OrderId, x.Kind,
        x.Body, x.ProviderMessageId, x.ProviderStatus, x.ProviderErrorCode, x.ProviderErrorMessage,
        x.CreatedAt, x.ScheduledFor, x.ProviderDateSent, x.ContentDisposedAt, x.SourceNotificationId);

    private static ContactNumberResult Map(ContactNumber x) => new(x.Id, x.CanonicalNumber, x.CreatedAt);
    private static void RequireBuyer(string buyerId)
    {
        if (string.IsNullOrWhiteSpace(buyerId)) throw new NotificationValidationException("The token has no shopper identity.");
    }

    private DateTimeOffset Now => _timeProvider.GetUtcNow();
}
