using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.OrderNotifications;

public sealed class OrderNotificationService
{
    private static readonly SemaphoreSlim ResendLock = new(1, 1);
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);
    private readonly CatalogContext _db;
    private readonly ITwilioMessageProvider _provider;
    private readonly ILogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        CatalogContext db,
        ITwilioMessageProvider provider,
        ILogger<OrderNotificationService> logger)
    {
        _db = db;
        _provider = provider;
        _logger = logger;
    }

    public async Task<ContactNumber> RegisterContactNumberAsync(string buyerId, string number, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(number))
        {
            throw new ApiOperationException(400, "A mobile number is required.");
        }

        PhoneValidationResult validation;
        try
        {
            validation = await _provider.ValidateDestinationAsync(number, ct);
        }
        catch (MessagingProviderException ex)
        {
            throw ProviderApiException(ex);
        }

        if (!validation.IsValid || string.IsNullOrWhiteSpace(validation.CanonicalNumber))
        {
            throw new ApiOperationException(400, "The messaging provider does not consider that number a usable destination.");
        }

        var existing = await _db.ContactNumbers.FirstOrDefaultAsync(
            x => x.BuyerId == buyerId && x.CanonicalNumber == validation.CanonicalNumber && x.DeletedAt == null, ct);
        if (existing is not null)
        {
            return existing;
        }

        var contact = new ContactNumber(buyerId, validation.CanonicalNumber, DateTimeOffset.UtcNow);
        _db.ContactNumbers.Add(contact);
        await _db.SaveChangesAsync(ct);
        return contact;
    }

    public Task<List<ContactNumber>> GetContactNumbersAsync(string buyerId, CancellationToken ct) =>
        _db.ContactNumbers.AsNoTracking()
            .Where(x => x.BuyerId == buyerId && x.DeletedAt == null)
            .OrderBy(x => x.Id)
            .ToListAsync(ct);

    public async Task RemoveContactNumberAsync(string buyerId, int contactNumberId, CancellationToken ct)
    {
        var contact = await _db.ContactNumbers.FirstOrDefaultAsync(
            x => x.Id == contactNumberId && x.BuyerId == buyerId && x.DeletedAt == null, ct);
        if (contact is null)
        {
            throw new ApiOperationException(404, "Contact number not found.");
        }

        contact.Remove(DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, PlaceOrderRequest request, CancellationToken ct)
    {
        if (request.Items is null || request.Items.Count == 0 || request.Items.Any(x => x.Quantity <= 0))
        {
            throw new ApiOperationException(400, "At least one catalog item with a positive quantity is required.");
        }

        if (request.Items.Select(x => x.CatalogItemId).Distinct().Count() != request.Items.Count)
        {
            throw new ApiOperationException(400, "Each catalog item may appear only once.");
        }

        if (request.ShippingAddress is null || !request.ShippingAddress.IsComplete())
        {
            throw new ApiOperationException(400, "A complete shipping address is required.");
        }

        var ids = request.Items.Select(x => x.CatalogItemId).ToArray();
        var catalog = await _db.CatalogItems.Where(x => ids.Contains(x.Id)).ToListAsync(ct);
        if (catalog.Count != ids.Length)
        {
            throw new ApiOperationException(400, "One or more catalog items do not exist.");
        }

        var quantities = request.Items.ToDictionary(x => x.CatalogItemId, x => x.Quantity);
        var orderItems = catalog.Select(item => new OrderItem(
            new CatalogItemOrdered(item.Id, item.Name, item.PictureUri),
            item.Price,
            quantities[item.Id])).ToList();
        var address = request.ShippingAddress.ToDomain();
        var order = new Order(buyerId, address, orderItems);

        _db.Orders.Add(order);
        await _db.SaveChangesAsync(ct);

        var contacts = await ActiveContactsAsync(buyerId, ct);
        foreach (var contact in contacts)
        {
            await SendAndRecordAsync(order, contact, NotificationKind.OrderPlaced,
                $"Your eShop order #{order.Id} has been placed.", null, ct);
        }

        return order;
    }

    public async Task<Order> DispatchOrderAsync(int orderId, CancellationToken ct)
    {
        var order = await _db.Orders.FirstOrDefaultAsync(x => x.Id == orderId, ct)
            ?? throw new ApiOperationException(404, "Order not found.");
        try
        {
            order.Dispatch(DateTimeOffset.UtcNow);
        }
        catch (InvalidOperationException ex)
        {
            throw new ApiOperationException(409, ex.Message);
        }

        await _db.SaveChangesAsync(ct);
        var contacts = await ActiveContactsAsync(order.BuyerId, ct);
        foreach (var contact in contacts)
        {
            await SendAndRecordAsync(order, contact, NotificationKind.OrderDispatched,
                $"Your eShop order #{order.Id} has been dispatched and is on its way.", null, ct);
            await SendAndRecordAsync(order, contact, NotificationKind.DeliveryFollowUp,
                $"How did delivery of eShop order #{order.Id} go?", DateTimeOffset.UtcNow.Add(FollowUpDelay), ct);
        }

        return order;
    }

    public async Task<Order> CancelOrderAsync(int orderId, CancellationToken ct)
    {
        var order = await _db.Orders.FirstOrDefaultAsync(x => x.Id == orderId, ct)
            ?? throw new ApiOperationException(404, "Order not found.");
        order.Cancel(DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync(ct);

        var followUps = await _db.OrderNotifications
            .Where(x => x.OrderId == orderId && x.Kind == NotificationKind.DeliveryFollowUp && x.ProviderMessageSid != null)
            .ToListAsync(ct);
        foreach (var followUp in followUps.Where(x => !IsTerminal(x.ProviderStatus)))
        {
            try
            {
                var state = await _provider.CancelAsync(followUp.ProviderMessageSid!, ct);
                followUp.ApplyProviderState(state, DateTimeOffset.UtcNow);
            }
            catch (MessagingProviderException ex)
            {
                followUp.MarkProviderStateStale(DateTimeOffset.UtcNow);
                _logger.LogWarning(
                    "Unable to cancel scheduled notification {NotificationId}; provider HTTP status {ProviderStatusCode}; its state requires reconciliation.",
                    followUp.Id,
                    ex.StatusCode);
            }
            await _db.SaveChangesAsync(ct);
        }

        var contacts = await ActiveContactsAsync(order.BuyerId, ct);
        foreach (var contact in contacts)
        {
            await SendAndRecordAsync(order, contact, NotificationKind.OrderCancelled,
                $"Your eShop order #{order.Id} has been cancelled.", null, ct);
        }

        return order;
    }

    public async Task<List<MyOrderDto>> GetMyOrdersAsync(string buyerId, CancellationToken ct)
    {
        var orders = await _db.Orders.AsNoTracking()
            .Include(x => x.OrderItems)
            .ThenInclude(x => x.ItemOrdered)
            .Where(x => x.BuyerId == buyerId)
            .OrderByDescending(x => x.OrderDate)
            .ToListAsync(ct);
        var notifications = await _db.OrderNotifications
            .Where(x => x.BuyerId == buyerId)
            .OrderBy(x => x.Id)
            .ToListAsync(ct);
        await RefreshNotificationsAsync(notifications, ct);

        return orders.Select(order => MyOrderDto.From(order,
            notifications.Where(x => x.OrderId == order.Id).Select(NotificationDto.From).ToList())).ToList();
    }

    public async Task<List<NotificationDto>> GetOrderNotificationsAsync(string buyerId, int orderId, CancellationToken ct)
    {
        var ownsOrder = await _db.Orders.AnyAsync(x => x.Id == orderId && x.BuyerId == buyerId, ct);
        if (!ownsOrder)
        {
            throw new ApiOperationException(404, "Order not found.");
        }

        var notifications = await _db.OrderNotifications
            .Where(x => x.OrderId == orderId && x.BuyerId == buyerId)
            .OrderBy(x => x.Id)
            .ToListAsync(ct);
        await RefreshNotificationsAsync(notifications, ct);
        return notifications.Select(NotificationDto.From).ToList();
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 128)
        {
            throw new ApiOperationException(400, "An idempotency key of at most 128 characters is required.");
        }

        await ResendLock.WaitAsync(ct);
        try
        {
            var existing = await _db.OrderNotifications.FirstOrDefaultAsync(
                x => x.OriginalNotificationId == notificationId && x.IdempotencyKey == idempotencyKey, ct);
            if (existing is not null)
            {
                return existing;
            }

            var original = await _db.OrderNotifications.FirstOrDefaultAsync(x => x.Id == notificationId, ct)
                ?? throw new ApiOperationException(404, "Notification not found.");
            if (original.ProviderMessageSid is not null)
            {
                try
                {
                    original.ApplyProviderState(await _provider.FetchAsync(original.ProviderMessageSid, ct), DateTimeOffset.UtcNow);
                    await _db.SaveChangesAsync(ct);
                }
                catch (MessagingProviderException)
                {
                    original.MarkProviderStateStale(DateTimeOffset.UtcNow);
                }
            }

            if (!CanResend(original.ProviderStatus))
            {
                throw new ApiOperationException(409, "Only a notification known not to have reached the shopper can be resent.");
            }
            if (string.IsNullOrWhiteSpace(original.Body))
            {
                throw new ApiOperationException(409, "Disposed notification content cannot be resent.");
            }

            var contact = await _db.ContactNumbers.FirstOrDefaultAsync(
                x => x.Id == original.ContactNumberId && x.BuyerId == original.BuyerId && x.DeletedAt == null, ct)
                ?? throw new ApiOperationException(409, "The destination is no longer registered.");

            var resend = new OrderNotification(
                original.OrderId, contact.Id, original.BuyerId, NotificationKind.Resend,
                original.Body, DateTimeOffset.UtcNow, original.Id, idempotencyKey);
            _db.OrderNotifications.Add(resend);
            await _db.SaveChangesAsync(ct);

            await SendExistingAsync(resend, contact.CanonicalNumber, null, ct);
            return resend;
        }
        finally
        {
            ResendLock.Release();
        }
    }

    public async Task DisposeNotificationContentAsync(int notificationId, CancellationToken ct)
    {
        var notification = await _db.OrderNotifications.FirstOrDefaultAsync(x => x.Id == notificationId, ct)
            ?? throw new ApiOperationException(404, "Notification not found.");
        if (notification.ContentDisposedAt is not null)
        {
            return;
        }

        if (notification.ProviderMessageSid is not null)
        {
            try
            {
                var state = await _provider.DisposeContentAsync(notification.ProviderMessageSid, ct);
                notification.ApplyProviderState(state, DateTimeOffset.UtcNow);
            }
            catch (MessagingProviderException ex)
            {
                throw ProviderApiException(ex);
            }
        }

        notification.DisposeContent(DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<ReconciliationResponse> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        if (from >= to)
        {
            throw new ApiOperationException(400, "The from date-time must be earlier than the to date-time.");
        }

        IReadOnlyList<ProviderMessageRecord> provider;
        try
        {
            provider = await _provider.ListAsync(from, to, ct);
        }
        catch (MessagingProviderException ex)
        {
            throw ProviderApiException(ex);
        }

        var local = await _db.OrderNotifications.AsNoTracking()
            .Where(x => (x.ProviderDateSent != null && x.ProviderDateSent >= from && x.ProviderDateSent <= to)
                || (x.ProviderDateSent == null && x.CreatedAt >= from && x.CreatedAt <= to))
            .ToListAsync(ct);
        var providerBySid = provider.ToDictionary(x => x.Sid, StringComparer.Ordinal);
        var localBySid = local.Where(x => x.ProviderMessageSid is not null)
            .ToDictionary(x => x.ProviderMessageSid!, StringComparer.Ordinal);
        var sids = providerBySid.Keys.Union(localBySid.Keys, StringComparer.Ordinal).OrderBy(x => x).ToList();
        var rows = sids.Select(sid => ReconciliationRow.From(
            providerBySid.GetValueOrDefault(sid), localBySid.GetValueOrDefault(sid))).ToList();
        rows.AddRange(local.Where(x => x.ProviderMessageSid is null)
            .Select(x => ReconciliationRow.From(null, x)));
        return new ReconciliationResponse(from, to, rows);
    }

    private async Task<List<ContactNumber>> ActiveContactsAsync(string buyerId, CancellationToken ct) =>
        await _db.ContactNumbers.Where(x => x.BuyerId == buyerId && x.DeletedAt == null).ToListAsync(ct);

    private async Task<OrderNotification> SendAndRecordAsync(
        Order order,
        ContactNumber contact,
        NotificationKind kind,
        string body,
        DateTimeOffset? scheduledFor,
        CancellationToken ct)
    {
        var notification = new OrderNotification(order.Id, contact.Id, order.BuyerId, kind, body, DateTimeOffset.UtcNow);
        _db.OrderNotifications.Add(notification);
        await _db.SaveChangesAsync(ct);
        return await SendExistingAsync(notification, contact.CanonicalNumber, scheduledFor, ct);
    }

    private async Task<OrderNotification> SendExistingAsync(
        OrderNotification notification,
        string canonicalNumber,
        DateTimeOffset? scheduledFor,
        CancellationToken ct)
    {
        try
        {
            var state = scheduledFor.HasValue
                ? await _provider.ScheduleAsync(canonicalNumber, notification.Body!, scheduledFor.Value, ct)
                : await _provider.SendAsync(canonicalNumber, notification.Body!, ct);
            notification.ApplyProviderState(state, DateTimeOffset.UtcNow);
        }
        catch (MessagingProviderException ex)
        {
            notification.RecordProviderFailure("provider_error", ex.Message, DateTimeOffset.UtcNow);
            _logger.LogWarning("Notification {NotificationId} could not be submitted to the provider.", notification.Id);
        }

        await _db.SaveChangesAsync(ct);
        return notification;
    }

    private async Task RefreshNotificationsAsync(List<OrderNotification> notifications, CancellationToken ct)
    {
        foreach (var notification in notifications.Where(x => x.ProviderMessageSid is not null))
        {
            try
            {
                var state = await _provider.FetchAsync(notification.ProviderMessageSid!, ct);
                notification.ApplyProviderState(state, DateTimeOffset.UtcNow);
            }
            catch (MessagingProviderException)
            {
                notification.MarkProviderStateStale(DateTimeOffset.UtcNow);
                _logger.LogWarning("Unable to refresh provider state for notification {NotificationId}.", notification.Id);
            }
        }
        await _db.SaveChangesAsync(ct);
    }

    private static bool IsTerminal(string status) => status is "delivered" or "failed" or "undelivered" or "canceled" or "read";
    private static bool CanResend(string status) => status is "failed" or "undelivered" or "canceled" or "provider_error";

    private static ApiOperationException ProviderApiException(MessagingProviderException ex) => ex.StatusCode switch
    {
        400 or 404 or 409 or 422 => new ApiOperationException(ex.StatusCode.Value, ex.Message),
        429 => new ApiOperationException(503, "The messaging provider is temporarily unavailable."),
        _ => new ApiOperationException(502, "The messaging provider is unavailable.")
    };
}

public sealed class ApiOperationException(int statusCode, string safeMessage) : Exception(safeMessage)
{
    public int StatusCode { get; } = statusCode;
}
