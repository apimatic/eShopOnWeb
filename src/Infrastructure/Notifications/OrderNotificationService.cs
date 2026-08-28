using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Notifications;

public sealed class OrderNotificationService : IOrderNotificationService
{
    private static readonly SemaphoreSlim ResendGate = new(1, 1);
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);
    private readonly CatalogContext _db;
    private readonly ITwilioMessagingGateway _twilio;
    private readonly ILogger<OrderNotificationService> _logger;

    public OrderNotificationService(CatalogContext db, ITwilioMessagingGateway twilio,
        ILogger<OrderNotificationService> logger)
    {
        _db = db;
        _twilio = twilio;
        _logger = logger;
    }

    public async Task<ContactNumberView> RegisterContactNumberAsync(string buyerId, string number,
        string? countryCode, CancellationToken ct)
    {
        var canonical = await _twilio.ValidateAndCanonicalizeAsync(number, countryCode, ct);
        if (canonical is null)
        {
            throw new InvalidContactNumberException();
        }

        var existing = await _db.ContactNumbers.SingleOrDefaultAsync(x =>
            x.BuyerId == buyerId && x.RemovedAt == null && x.CanonicalNumber == canonical, ct);
        if (existing is not null)
        {
            return ToView(existing);
        }

        var contact = new ContactNumber(buyerId, canonical, DateTimeOffset.UtcNow);
        _db.ContactNumbers.Add(contact);
        await _db.SaveChangesAsync(ct);
        return ToView(contact);
    }

    public async Task<IReadOnlyList<ContactNumberView>> GetContactNumbersAsync(string buyerId, CancellationToken ct) =>
        await _db.ContactNumbers.AsNoTracking()
            .Where(x => x.BuyerId == buyerId && x.RemovedAt == null && x.CanonicalNumber != null)
            .OrderBy(x => x.Id)
            .Select(x => new ContactNumberView(x.Id, x.CanonicalNumber!, x.CreatedAt))
            .ToListAsync(ct);

    public async Task<bool> DeleteContactNumberAsync(string buyerId, int contactNumberId, CancellationToken ct)
    {
        var contact = await _db.ContactNumbers.SingleOrDefaultAsync(x =>
            x.Id == contactNumberId && x.BuyerId == buyerId && x.RemovedAt == null, ct);
        if (contact is null)
        {
            return false;
        }

        var scheduled = await _db.OrderNotifications.Where(x =>
            x.ContactNumberId == contactNumberId &&
            x.Kind == NotificationKind.DeliveryFollowUp &&
            x.CancellationCompletedAt == null &&
            x.ProviderMessageSid != null &&
            x.ProviderStatus != "delivered" && x.ProviderStatus != "sent" &&
            x.ProviderStatus != "canceled" && x.ProviderStatus != "undelivered" &&
            (x.ProviderStatus != "failed" || x.CancellationRequestedAt != null)).ToListAsync(ct);

        foreach (var notification in scheduled)
        {
            notification.RequestCancellation(DateTimeOffset.UtcNow);
        }
        await _db.SaveChangesAsync(ct);

        // A successful DELETE promises that provider-owned future sends to this number are gone.
        // Therefore a provider cancellation failure rejects the deletion instead of returning a false success.
        foreach (var notification in scheduled)
        {
            var provider = await _twilio.CancelAsync(notification.ProviderMessageSid!, ct);
            notification.CompleteCancellation(provider.Status, DateTimeOffset.UtcNow);
        }

        contact.Remove(DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<int> PlaceOrderAsync(string buyerId, PlaceOrderCommand command, CancellationToken ct)
    {
        if (command.Items.Count == 0 || command.Items.Any(x => x.CatalogItemId <= 0 || x.Quantity <= 0))
        {
            throw new ArgumentException("At least one catalog item with a positive quantity is required.");
        }

        var lines = command.Items.GroupBy(x => x.CatalogItemId)
            .Select(x => new PlaceOrderLine(x.Key, x.Sum(y => y.Quantity))).ToList();
        var ids = lines.Select(x => x.CatalogItemId).ToArray();
        var catalogItems = await _db.CatalogItems.Where(x => ids.Contains(x.Id)).ToListAsync(ct);
        if (catalogItems.Count != ids.Length)
        {
            throw new ArgumentException("One or more catalog items do not exist.");
        }

        var items = lines.Select(line =>
        {
            var item = catalogItems.Single(x => x.Id == line.CatalogItemId);
            return new OrderItem(new CatalogItemOrdered(item.Id, item.Name, item.PictureUri), item.Price, line.Quantity);
        }).ToList();
        var address = command.ShippingAddress;
        var order = new Order(buyerId,
            new Address(address.Street, address.City, address.State, address.Country, address.ZipCode), items);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(ct);

        await NotifyWithoutFailingOperationAsync(order.Id, buyerId, NotificationKind.OrderPlaced,
            $"Your eShop order #{order.Id} has been placed.", null, ct);
        return order.Id;
    }

    public async Task<bool> DispatchOrderAsync(int orderId, CancellationToken ct)
    {
        var order = await _db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, ct);
        if (order is null)
        {
            return false;
        }

        if (!order.Dispatch(DateTimeOffset.UtcNow))
        {
            return true;
        }
        await _db.SaveChangesAsync(ct);

        await NotifyWithoutFailingOperationAsync(order.Id, order.BuyerId, NotificationKind.OrderDispatched,
            $"Your eShop order #{order.Id} is on its way.", null, ct);
        await NotifyWithoutFailingOperationAsync(order.Id, order.BuyerId, NotificationKind.DeliveryFollowUp,
            $"How did delivery of your eShop order #{order.Id} go?", DateTimeOffset.UtcNow.Add(FollowUpDelay), ct);
        return true;
    }

    public async Task<bool> CancelOrderAsync(int orderId, CancellationToken ct)
    {
        var order = await _db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, ct);
        if (order is null)
        {
            return false;
        }

        var changed = order.Cancel(DateTimeOffset.UtcNow);
        var followUps = await _db.OrderNotifications.Where(x =>
            x.OrderId == orderId && x.Kind == NotificationKind.DeliveryFollowUp &&
            x.CancellationCompletedAt == null && x.ProviderMessageSid != null).ToListAsync(ct);
        foreach (var followUp in followUps)
        {
            followUp.RequestCancellation(DateTimeOffset.UtcNow);
        }
        await _db.SaveChangesAsync(ct);

        foreach (var followUp in followUps)
        {
            try
            {
                var provider = await _twilio.CancelAsync(followUp.ProviderMessageSid!, ct);
                followUp.CompleteCancellation(provider.Status, DateTimeOffset.UtcNow);
                await _db.SaveChangesAsync(ct);
            }
            catch (TwilioProviderException ex)
            {
                followUp.RecordFailure("Provider cancellation is pending retry.", ex.StatusCode);
                await SaveNotificationStateBestEffortAsync(ct);
                _logger.LogWarning("A scheduled notification cancellation for order {OrderId} is pending retry.", orderId);
            }
        }

        if (changed)
        {
            await NotifyWithoutFailingOperationAsync(order.Id, order.BuyerId, NotificationKind.OrderCancelled,
                $"Your eShop order #{order.Id} has been cancelled.", null, ct);
        }
        return true;
    }

    public async Task<IReadOnlyList<OrderView>> GetMyOrdersAsync(string buyerId, CancellationToken ct)
    {
        await RetryPendingCancellationsAsync(ct);
        var orders = await _db.Orders.AsNoTracking().Include(x => x.OrderItems)
            .Where(x => x.BuyerId == buyerId).OrderByDescending(x => x.OrderDate).ToListAsync(ct);
        var orderIds = orders.Select(x => x.Id).ToArray();
        var notifications = await _db.OrderNotifications.Where(x => orderIds.Contains(x.OrderId)).ToListAsync(ct);
        await RefreshProviderStatesAsync(notifications, ct);

        return orders.Select(order => new OrderView(order.Id, order.OrderDate, order.Progress.ToString(), order.Total(),
            notifications.Where(x => x.OrderId == order.Id).OrderBy(x => x.CreatedAt).Select(ToView).ToList())).ToList();
    }

    public async Task<IReadOnlyList<NotificationView>?> GetOrderNotificationsAsync(string buyerId, int orderId,
        CancellationToken ct)
    {
        var ownsOrder = await _db.Orders.AnyAsync(x => x.Id == orderId && x.BuyerId == buyerId, ct);
        if (!ownsOrder)
        {
            return null;
        }

        await RetryPendingCancellationsAsync(ct);
        var notifications = await _db.OrderNotifications.Where(x => x.OrderId == orderId)
            .OrderBy(x => x.CreatedAt).ToListAsync(ct);
        await RefreshProviderStatesAsync(notifications, ct);
        return notifications.Select(ToView).ToList();
    }

    public async Task<int> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 200)
        {
            throw new ArgumentException("An idempotency key of 1 to 200 characters is required.");
        }

        await ResendGate.WaitAsync(ct);
        try
        {
            var existing = await _db.NotificationResends.SingleOrDefaultAsync(x =>
                x.SourceNotificationId == notificationId && x.IdempotencyKey == idempotencyKey, ct);
            if (existing?.ResultNotificationId is { } existingResult)
            {
                return existingResult;
            }

            var source = await _db.OrderNotifications.SingleOrDefaultAsync(x => x.Id == notificationId, ct)
                ?? throw new KeyNotFoundException("Notification not found.");
            if (source.ProviderMessageSid is not null)
            {
                try
                {
                    ApplyProviderState(source, await _twilio.FetchAsync(source.ProviderMessageSid, ct));
                    await _db.SaveChangesAsync(ct);
                }
                catch (TwilioProviderException)
                {
                    // The last known status is still usable for an explicitly requested operator retry.
                }
            }

            if (!CanResend(source.ProviderStatus))
            {
                throw new NotificationConflictException("Only failed, undelivered, or cancelled notifications can be resent.");
            }
            if (string.IsNullOrWhiteSpace(source.Content))
            {
                throw new NotificationConflictException("A notification whose content was disposed cannot be resent.");
            }

            var contact = await _db.ContactNumbers.SingleOrDefaultAsync(x =>
                x.Id == source.ContactNumberId && x.RemovedAt == null && x.CanonicalNumber != null, ct);
            if (contact is null)
            {
                throw new NotificationConflictException("The destination is no longer registered.");
            }

            var claim = existing ?? new NotificationResend(notificationId, idempotencyKey, DateTimeOffset.UtcNow);
            if (existing is null)
            {
                _db.NotificationResends.Add(claim);
            }
            var result = new OrderNotification(source.OrderId, source.ContactNumberId, NotificationKind.Resend,
                source.Content, DateTimeOffset.UtcNow, resendsNotificationId: source.Id);
            _db.OrderNotifications.Add(result);
            await _db.SaveChangesAsync(ct);
            claim.Complete(result.Id);
            await _db.SaveChangesAsync(ct); // Claim result before the external write.

            await SendExistingNotificationAsync(result, contact.CanonicalNumber!, null, ct);
            return result.Id;
        }
        finally
        {
            ResendGate.Release();
        }
    }

    public async Task<bool> DisposeContentAsync(int notificationId, CancellationToken ct)
    {
        var notification = await _db.OrderNotifications.SingleOrDefaultAsync(x => x.Id == notificationId, ct);
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
            var redacted = await _twilio.RedactAsync(notification.ProviderMessageSid, ct);
            if (!string.IsNullOrEmpty(redacted.Body))
            {
                throw new TwilioProviderException("Twilio did not confirm message-content disposal.");
            }

            try
            {
                var verification = await _twilio.FetchAsync(notification.ProviderMessageSid, ct);
                if (!string.IsNullOrEmpty(verification.Body))
                {
                    throw new TwilioProviderException("Twilio still returned message content after disposal.");
                }
                ApplyProviderState(notification, verification);
            }
            catch (TwilioProviderException ex) when (ex.Message.Contains("could not be reached", StringComparison.Ordinal))
            {
                // UpdateMessage already succeeded; verification is deliberately best-effort.
            }
        }

        notification.DisposeContent(DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<ReconciliationView> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        if (from > to)
        {
            throw new ArgumentException("The from instant must not be after to.");
        }

        var provider = await _twilio.ListAsync(from, to, ct);
        var local = await _db.OrderNotifications.AsNoTracking()
            .Where(x => (x.ProviderDateSent ?? x.ProviderDateCreated ?? x.CreatedAt) >= from &&
                        (x.ProviderDateSent ?? x.ProviderDateCreated ?? x.CreatedAt) <= to &&
                        x.ProviderMessageSid != null)
            .ToListAsync(ct);
        var localBySid = local.ToDictionary(x => x.ProviderMessageSid!, StringComparer.Ordinal);
        var providerBySid = provider.ToDictionary(x => x.ProviderMessageId, StringComparer.Ordinal);
        var entries = new List<ReconciliationEntry>();

        foreach (var message in provider.OrderBy(x => x.DateSent))
        {
            localBySid.TryGetValue(message.ProviderMessageId, out var match);
            entries.Add(new ReconciliationEntry(message.ProviderMessageId, match?.Id,
                match is null ? "provider-only" : "matched", message.Status, message.DateSent));
        }
        foreach (var notification in local.Where(x => !providerBySid.ContainsKey(x.ProviderMessageSid!)))
        {
            entries.Add(new ReconciliationEntry(notification.ProviderMessageSid!, notification.Id,
                "application-only", notification.ProviderStatus, notification.ProviderDateSent));
        }

        return new ReconciliationView(from, to, entries);
    }

    private async Task NotifyWithoutFailingOperationAsync(int orderId, string buyerId, NotificationKind kind,
        string content, DateTimeOffset? scheduledFor, CancellationToken ct)
    {
        try
        {
            var contacts = await _db.ContactNumbers.Where(x =>
                x.BuyerId == buyerId && x.RemovedAt == null && x.CanonicalNumber != null).ToListAsync(ct);
            foreach (var contact in contacts)
            {
                var notification = new OrderNotification(orderId, contact.Id, kind, content,
                    DateTimeOffset.UtcNow, scheduledFor);
                _db.OrderNotifications.Add(notification);
                await _db.SaveChangesAsync(ct);
                await SendExistingNotificationAsync(notification, contact.CanonicalNumber!, scheduledFor, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Order {OrderId} completed, but one or more notifications could not be recorded or sent.", orderId);
        }
    }

    private async Task SendExistingNotificationAsync(OrderNotification notification, string destination,
        DateTimeOffset? scheduledFor, CancellationToken ct)
    {
        try
        {
            var provider = scheduledFor is null
                ? await _twilio.SendAsync(destination, notification.Content!, ct)
                : await _twilio.ScheduleAsync(destination, notification.Content!, scheduledFor.Value, ct);
            ApplyProviderState(notification, provider);
        }
        catch (TwilioProviderException ex)
        {
            notification.RecordFailure("The messaging provider did not accept this attempt.", ex.StatusCode);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            notification.RecordFailure("The notification attempt failed before provider confirmation.");
        }
        await SaveNotificationStateBestEffortAsync(ct);
    }

    private async Task RefreshProviderStatesAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken ct)
    {
        foreach (var notification in notifications.Where(x => x.ProviderMessageSid != null))
        {
            try
            {
                ApplyProviderState(notification, await _twilio.FetchAsync(notification.ProviderMessageSid!, ct));
            }
            catch (TwilioProviderException)
            {
                // Reads expose the last durable outcome when provider polling is temporarily unavailable.
            }
        }
        await SaveNotificationStateBestEffortAsync(ct);
    }

    private async Task RetryPendingCancellationsAsync(CancellationToken ct)
    {
        var pending = await _db.OrderNotifications.Where(x =>
            x.CancellationRequestedAt != null && x.CancellationCompletedAt == null && x.ProviderMessageSid != null)
            .ToListAsync(ct);
        foreach (var notification in pending)
        {
            try
            {
                var provider = await _twilio.CancelAsync(notification.ProviderMessageSid!, ct);
                notification.CompleteCancellation(provider.Status, DateTimeOffset.UtcNow);
            }
            catch (TwilioProviderException)
            {
                // Durable intent remains for the next operator/shopper read and background retry.
            }
        }
        await SaveNotificationStateBestEffortAsync(ct);
    }

    private async Task SaveNotificationStateBestEffortAsync(CancellationToken ct)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Notification provider state could not be persisted.");
        }
    }

    private static void ApplyProviderState(OrderNotification notification, ProviderMessage provider) =>
        notification.RecordProviderState(provider.ProviderMessageId, provider.Status, provider.ErrorCode,
            provider.DateCreated, provider.DateSent, provider.DateUpdated);

    private static bool CanResend(string status) => status is "failed" or "undelivered" or "canceled";
    private static ContactNumberView ToView(ContactNumber contact) =>
        new(contact.Id, contact.CanonicalNumber!, contact.CreatedAt);
    private static NotificationView ToView(OrderNotification notification) =>
        new(notification.Id, notification.Kind, notification.ProviderStatus, notification.Content,
            notification.ProviderMessageSid, notification.ProviderErrorCode, notification.CreatedAt,
            notification.ScheduledFor, notification.ContentDisposedAt, notification.ResendsNotificationId);
}
