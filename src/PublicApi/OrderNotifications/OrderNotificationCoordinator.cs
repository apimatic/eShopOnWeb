using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.OrderNotifications;

public sealed class OrderNotificationCoordinator
{
    private const int MaximumContactNumbers = 5;
    private static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);
    private readonly CatalogContext _db;
    private readonly ITextMessageProvider _provider;
    private readonly TimeProvider _clock;
    private readonly ApplicationOperationLock _operationLock;
    private readonly ILogger<OrderNotificationCoordinator> _logger;

    public OrderNotificationCoordinator(
        CatalogContext db,
        ITextMessageProvider provider,
        TimeProvider clock,
        ApplicationOperationLock operationLock,
        ILogger<OrderNotificationCoordinator> logger)
    {
        _db = db;
        _provider = provider;
        _clock = clock;
        _operationLock = operationLock;
        _logger = logger;
    }

    public async Task<RegisterContactNumberResponse> RegisterContactNumberAsync(
        string buyerId,
        RegisterContactNumberRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            throw new OrderNotificationApiException(400, "phoneNumber is required.");
        }

        string? canonical;
        try
        {
            canonical = await _provider.ValidateAndCanonicalizeAsync(request.PhoneNumber, cancellationToken);
        }
        catch (MessagingProviderException ex)
        {
            throw MapProviderException(ex, invalidDestinationIsBadRequest: true);
        }

        if (string.IsNullOrWhiteSpace(canonical))
        {
            throw new OrderNotificationApiException(400, "The provider does not consider this a usable destination.");
        }

        using var operation = await _operationLock.AcquireAsync($"contact:{buyerId}", cancellationToken);
        if (await _db.ContactNumbers.AnyAsync(x => x.BuyerId == buyerId && x.CanonicalNumber == canonical, cancellationToken))
        {
            throw new OrderNotificationApiException(409, "That contact number is already registered.");
        }

        if (await _db.ContactNumbers.CountAsync(x => x.BuyerId == buyerId, cancellationToken) >= MaximumContactNumbers)
        {
            throw new OrderNotificationApiException(409, $"At most {MaximumContactNumbers} contact numbers may be registered.");
        }

        var contact = new ContactNumber(buyerId, canonical, _clock.GetUtcNow());
        _db.ContactNumbers.Add(contact);
        await _db.SaveChangesAsync(cancellationToken);
        return new RegisterContactNumberResponse(contact.Id, contact.CanonicalNumber);
    }

    public async Task<ContactNumberListResponse> ListContactNumbersAsync(string buyerId, CancellationToken cancellationToken)
    {
        var contacts = await _db.ContactNumbers.AsNoTracking()
            .Where(x => x.BuyerId == buyerId)
            .OrderBy(x => x.Id)
            .Select(x => new ContactNumberDto(x.Id, x.CanonicalNumber, x.CreatedAt))
            .ToListAsync(cancellationToken);
        return new ContactNumberListResponse(contacts);
    }

    public async Task DeleteContactNumberAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken)
    {
        using var operation = await _operationLock.AcquireAsync($"contact:{buyerId}", cancellationToken);
        var contact = await _db.ContactNumbers.SingleOrDefaultAsync(
            x => x.Id == contactNumberId && x.BuyerId == buyerId,
            cancellationToken);
        if (contact is null)
        {
            throw new OrderNotificationApiException(404, "Contact number not found.");
        }

        var scheduled = await _db.OrderNotifications
            .Where(x => x.ContactNumberId == contactNumberId &&
                        x.Kind == NotificationKind.DeliveryFollowUp &&
                        x.ProviderMessageSid != null &&
                        x.ProviderStatus != "canceled" &&
                        x.ProviderStatus != "delivered" &&
                        x.ProviderStatus != "undelivered" &&
                        x.ProviderStatus != "failed")
            .ToListAsync(cancellationToken);

        foreach (var notification in scheduled)
        {
            await CancelScheduledNotificationAsync(notification, cancellationToken, failOperation: true);
        }

        _db.ContactNumbers.Remove(contact);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<PlaceOrderResponse> PlaceOrderAsync(
        string buyerId,
        PlaceOrderRequest request,
        CancellationToken cancellationToken)
    {
        ValidateOrderRequest(request);
        var requested = request.Items.GroupBy(x => x.CatalogItemId)
            .Select(x => new { CatalogItemId = x.Key, Quantity = x.Sum(y => y.Quantity) })
            .ToList();
        var ids = requested.Select(x => x.CatalogItemId).ToArray();
        var catalogItems = await _db.CatalogItems.Where(x => ids.Contains(x.Id)).ToListAsync(cancellationToken);
        if (catalogItems.Count != ids.Length)
        {
            throw new OrderNotificationApiException(400, "One or more catalog items do not exist.");
        }

        var orderItems = requested.Select(line =>
        {
            var catalogItem = catalogItems.Single(x => x.Id == line.CatalogItemId);
            return new OrderItem(
                new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri),
                catalogItem.Price,
                line.Quantity);
        }).ToList();
        var address = request.ShippingAddress;
        var order = new Order(buyerId,
            new Address(address.Street, address.City, address.State, address.Country, address.ZipCode),
            orderItems);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);

        await SendToRegisteredContactsAsync(
            order,
            NotificationKind.OrderPlaced,
            $"Your order #{order.Id} has been placed.",
            null,
            cancellationToken);
        return new PlaceOrderResponse(order.Id);
    }

    public async Task DispatchOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        using var operation = await _operationLock.AcquireAsync($"order:{orderId}", cancellationToken);
        var order = await _db.Orders.Include(x => x.OrderItems).SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        if (order is null)
        {
            throw new OrderNotificationApiException(404, "Order not found.");
        }

        var now = _clock.GetUtcNow();
        if (!order.Dispatch(now))
        {
            throw new OrderNotificationApiException(409, $"An order in {order.Status} state cannot be dispatched.");
        }
        await _db.SaveChangesAsync(cancellationToken);

        await SendToRegisteredContactsAsync(
            order,
            NotificationKind.OrderDispatched,
            $"Your order #{order.Id} has been dispatched and is on its way.",
            null,
            cancellationToken);
        await SendToRegisteredContactsAsync(
            order,
            NotificationKind.DeliveryFollowUp,
            $"How did the delivery of order #{order.Id} go?",
            now.Add(DeliveryFollowUpDelay),
            cancellationToken);
    }

    public async Task CancelOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        using var operation = await _operationLock.AcquireAsync($"order:{orderId}", cancellationToken);
        var order = await _db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        if (order is null)
        {
            throw new OrderNotificationApiException(404, "Order not found.");
        }
        if (order.Status == OrderStatus.Cancelled)
        {
            return;
        }

        var followUps = await _db.OrderNotifications
            .Where(x => x.OrderId == orderId &&
                        x.Kind == NotificationKind.DeliveryFollowUp &&
                        x.ProviderMessageSid != null &&
                        x.ProviderStatus != "canceled" &&
                        x.ProviderStatus != "delivered" &&
                        x.ProviderStatus != "undelivered" &&
                        x.ProviderStatus != "failed")
            .ToListAsync(cancellationToken);
        foreach (var followUp in followUps)
        {
            await CancelScheduledNotificationAsync(followUp, cancellationToken, failOperation: true);
        }

        order.Cancel(_clock.GetUtcNow());
        await _db.SaveChangesAsync(cancellationToken);
        await SendToRegisteredContactsAsync(
            order,
            NotificationKind.OrderCancelled,
            $"Your order #{order.Id} has been cancelled.",
            null,
            cancellationToken);
    }

    public async Task<MyOrdersResponse> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        var orders = await _db.Orders.AsNoTracking()
            .Include(x => x.OrderItems)
            .Where(x => x.BuyerId == buyerId)
            .OrderByDescending(x => x.OrderDate)
            .ToListAsync(cancellationToken);
        var orderIds = orders.Select(x => x.Id).ToArray();
        var notifications = await _db.OrderNotifications.Where(x => orderIds.Contains(x.OrderId)).ToListAsync(cancellationToken);
        await RefreshNotificationsBestEffortAsync(notifications, cancellationToken);

        return new MyOrdersResponse(orders.Select(order => new OrderDto(
            order.Id,
            order.OrderDate,
            order.Status.ToString(),
            order.Total(),
            notifications.Where(x => x.OrderId == order.Id).OrderBy(x => x.CreatedAt).Select(MapNotification).ToList())).ToList());
    }

    public async Task<OrderNotificationsResponse> GetOrderNotificationsAsync(
        string buyerId,
        int orderId,
        CancellationToken cancellationToken)
    {
        if (!await _db.Orders.AnyAsync(x => x.Id == orderId && x.BuyerId == buyerId, cancellationToken))
        {
            throw new OrderNotificationApiException(404, "Order not found.");
        }
        var notifications = await _db.OrderNotifications.Where(x => x.OrderId == orderId).OrderBy(x => x.CreatedAt).ToListAsync(cancellationToken);
        await RefreshNotificationsBestEffortAsync(notifications, cancellationToken);
        return new OrderNotificationsResponse(orderId, notifications.Select(MapNotification).ToList());
    }

    public async Task<ResendNotificationResponse> ResendAsync(
        int sourceNotificationId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 256)
        {
            throw new OrderNotificationApiException(400, "A non-empty idempotencyKey of at most 256 characters is required.");
        }
        var keyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(idempotencyKey)));
        using var operation = await _operationLock.AcquireAsync($"resend:{sourceNotificationId}:{keyHash}", cancellationToken);

        var existing = await _db.NotificationResends.SingleOrDefaultAsync(
            x => x.SourceNotificationId == sourceNotificationId && x.IdempotencyKey == keyHash,
            cancellationToken);
        if (existing?.ResultNotificationId is int existingId)
        {
            return new ResendNotificationResponse(existingId);
        }

        var source = await _db.OrderNotifications.SingleOrDefaultAsync(x => x.Id == sourceNotificationId, cancellationToken);
        if (source is null)
        {
            throw new OrderNotificationApiException(404, "Notification not found.");
        }
        if (source.IsContentDisposed || string.IsNullOrWhiteSpace(source.Body))
        {
            throw new OrderNotificationApiException(409, "A notification with disposed content cannot be resent.");
        }

        await RefreshNotificationBestEffortAsync(source, cancellationToken);
        if (!IsResendable(source.ProviderStatus))
        {
            throw new OrderNotificationApiException(409, "Only a notification that failed or was undelivered can be resent.");
        }

        using var contactOperation = await _operationLock.AcquireAsync($"contact:{source.BuyerId}", cancellationToken);
        var contact = await _db.ContactNumbers.SingleOrDefaultAsync(
            x => x.Id == source.ContactNumberId && x.BuyerId == source.BuyerId,
            cancellationToken);
        if (contact is null)
        {
            throw new OrderNotificationApiException(409, "The destination is no longer registered and cannot be used again.");
        }

        existing ??= new NotificationResend(sourceNotificationId, keyHash, _clock.GetUtcNow());
        if (existing.Id == 0)
        {
            _db.NotificationResends.Add(existing);
            await _db.SaveChangesAsync(cancellationToken);
        }

        var result = new OrderNotification(
            source.OrderId,
            source.BuyerId,
            source.ContactNumberId,
            NotificationKind.Resend,
            source.Body,
            _clock.GetUtcNow(),
            source.Id);
        _db.OrderNotifications.Add(result);
        await _db.SaveChangesAsync(cancellationToken);
        existing.SetResult(result.Id);
        await _db.SaveChangesAsync(cancellationToken);

        await DeliverAsync(result, contact.CanonicalNumber, null, cancellationToken);
        return new ResendNotificationResponse(result.Id);
    }

    public async Task<ContentDisposalResponse> DisposeContentAsync(int notificationId, CancellationToken cancellationToken)
    {
        using var operation = await _operationLock.AcquireAsync($"dispose:{notificationId}", cancellationToken);
        var notification = await _db.OrderNotifications.SingleOrDefaultAsync(x => x.Id == notificationId, cancellationToken);
        if (notification is null)
        {
            throw new OrderNotificationApiException(404, "Notification not found.");
        }
        if (notification.IsContentDisposed)
        {
            return new ContentDisposalResponse(notification.Id, true);
        }

        if (!string.IsNullOrWhiteSpace(notification.ProviderMessageSid))
        {
            ProviderMessageSnapshot providerState;
            try
            {
                providerState = await _provider.RedactAsync(notification.ProviderMessageSid, cancellationToken);
            }
            catch (MessagingProviderException ex)
            {
                throw MapProviderException(ex);
            }
            ApplyProviderState(notification, providerState);
        }

        notification.MarkContentDisposed(_clock.GetUtcNow());
        await _db.SaveChangesAsync(cancellationToken);
        return new ContentDisposalResponse(notification.Id, true);
    }

    public async Task<ReconciliationResponse> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (from > to)
        {
            throw new OrderNotificationApiException(400, "from must be earlier than or equal to to.");
        }

        IReadOnlyList<ProviderMessageSnapshot> providerMessages;
        try
        {
            providerMessages = await _provider.ListAsync(from, to, cancellationToken);
        }
        catch (MessagingProviderException ex)
        {
            throw MapProviderException(ex);
        }

        var local = await _db.OrderNotifications.AsNoTracking()
            .Where(x => x.CreatedAt >= from && x.CreatedAt <= to)
            .ToListAsync(cancellationToken);
        var localBySid = local.Where(x => x.ProviderMessageSid is not null)
            .ToDictionary(x => x.ProviderMessageSid!, StringComparer.Ordinal);
        var providerSids = providerMessages.Where(x => x.Sid is not null).Select(x => x.Sid!).Distinct(StringComparer.Ordinal).ToArray();
        foreach (var chunk in providerSids.Chunk(500))
        {
            var missing = await _db.OrderNotifications.AsNoTracking()
                .Where(x => x.ProviderMessageSid != null && chunk.Contains(x.ProviderMessageSid))
                .ToListAsync(cancellationToken);
            foreach (var notification in missing)
            {
                if (notification.ProviderMessageSid is not null)
                {
                    localBySid[notification.ProviderMessageSid] = notification;
                }
            }
        }

        var rows = new List<ReconciliationEntryDto>();
        var matchedLocalIds = new HashSet<int>();
        foreach (var providerMessage in providerMessages)
        {
            localBySid.TryGetValue(providerMessage.Sid ?? string.Empty, out var applicationMessage);
            if (applicationMessage is not null)
            {
                matchedLocalIds.Add(applicationMessage.Id);
            }
            rows.Add(new ReconciliationEntryDto(
                applicationMessage is null ? "providerOnly" : "matched",
                applicationMessage?.Id,
                providerMessage.Sid,
                applicationMessage?.ProviderStatus,
                providerMessage.Status,
                providerMessage.DateCreated,
                providerMessage.DateSent,
                providerMessage.ErrorCode));
        }

        foreach (var applicationMessage in local.Where(x => !matchedLocalIds.Contains(x.Id)))
        {
            rows.Add(new ReconciliationEntryDto(
                "applicationOnly",
                applicationMessage.Id,
                applicationMessage.ProviderMessageSid,
                applicationMessage.ProviderStatus,
                null,
                applicationMessage.ProviderDateCreated,
                applicationMessage.ProviderDateSent,
                applicationMessage.ProviderErrorCode));
        }

        return new ReconciliationResponse(from, to, rows);
    }

    private async Task SendToRegisteredContactsAsync(
        Order order,
        NotificationKind kind,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        using var contactOperation = await _operationLock.AcquireAsync($"contact:{order.BuyerId}", cancellationToken);
        var contacts = await _db.ContactNumbers.Where(x => x.BuyerId == order.BuyerId).OrderBy(x => x.Id).ToListAsync(cancellationToken);
        foreach (var contact in contacts)
        {
            var notification = new OrderNotification(order.Id, order.BuyerId, contact.Id, kind, body, _clock.GetUtcNow());
            _db.OrderNotifications.Add(notification);
            await _db.SaveChangesAsync(cancellationToken);
            await DeliverAsync(notification, contact.CanonicalNumber, sendAt, cancellationToken);
        }
    }

    private async Task DeliverAsync(
        OrderNotification notification,
        string destination,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var state = sendAt.HasValue
                ? await _provider.ScheduleAsync(destination, notification.Body!, sendAt.Value, cancellationToken)
                : await _provider.SendAsync(destination, notification.Body!, cancellationToken);
            ApplyProviderState(notification, state, sendAt);
            _logger.LogInformation("Notification {NotificationId} received provider state {ProviderStatus}.", notification.Id, notification.ProviderStatus);
        }
        catch (MessagingProviderException ex)
        {
            notification.MarkProviderFailure(ex.StatusCode, ex.Message, _clock.GetUtcNow());
            _logger.LogWarning("Notification {NotificationId} could not be submitted to the provider; status {ProviderStatusCode}.", notification.Id, ex.StatusCode);
        }
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task CancelScheduledNotificationAsync(
        OrderNotification notification,
        CancellationToken cancellationToken,
        bool failOperation)
    {
        notification.MarkCancellationRequested(_clock.GetUtcNow());
        try
        {
            var state = await _provider.CancelAsync(notification.ProviderMessageSid!, cancellationToken);
            ApplyProviderState(notification, state);
            if (!string.Equals(notification.ProviderStatus, "canceled", StringComparison.OrdinalIgnoreCase))
            {
                throw new MessagingProviderException("The provider did not confirm scheduled-message cancellation.", 502);
            }
        }
        catch (MessagingProviderException ex)
        {
            notification.MarkCancellationFailure(ex.StatusCode, ex.Message, _clock.GetUtcNow());
            await _db.SaveChangesAsync(cancellationToken);
            if (failOperation)
            {
                throw MapProviderException(ex);
            }
            return;
        }
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task RefreshNotificationsBestEffortAsync(List<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            await RefreshNotificationBestEffortAsync(notification, cancellationToken);
        }
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task RefreshNotificationBestEffortAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(notification.ProviderMessageSid))
        {
            return;
        }
        try
        {
            ApplyProviderState(notification, await _provider.FetchAsync(notification.ProviderMessageSid, cancellationToken));
        }
        catch (MessagingProviderException)
        {
            notification.MarkRefreshFailure(_clock.GetUtcNow());
            _logger.LogWarning("Provider state refresh failed for notification {NotificationId}.", notification.Id);
        }
    }

    private void ApplyProviderState(OrderNotification notification, ProviderMessageSnapshot state, DateTimeOffset? scheduledFor = null)
    {
        notification.ApplyProviderState(
            state.Sid,
            state.Status,
            state.Direction,
            state.ErrorCode,
            state.ErrorMessage,
            state.DateCreated,
            state.DateSent,
            state.DateUpdated,
            _clock.GetUtcNow(),
            scheduledFor);
    }

    private static NotificationDto MapNotification(OrderNotification notification) => new(
        notification.Id,
        notification.OrderId,
        notification.Kind.ToString(),
        notification.ProviderStatus,
        notification.ProviderMessageSid,
        notification.ProviderErrorCode,
        notification.CreatedAt,
        notification.ScheduledFor,
        notification.IsContentDisposed,
        notification.LastRefreshFailedAt.HasValue,
        notification.CancellationFailedAt.HasValue,
        notification.OriginalNotificationId);

    private static bool IsResendable(string status) =>
        string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "undelivered", StringComparison.OrdinalIgnoreCase);

    private static void ValidateOrderRequest(PlaceOrderRequest request)
    {
        if (request.Items is null || request.Items.Count == 0 || request.Items.Any(x => x.CatalogItemId <= 0 || x.Quantity <= 0))
        {
            throw new OrderNotificationApiException(400, "At least one catalog item with a positive quantity is required.");
        }
        if (request.ShippingAddress is null ||
            string.IsNullOrWhiteSpace(request.ShippingAddress.Street) ||
            string.IsNullOrWhiteSpace(request.ShippingAddress.City) ||
            string.IsNullOrWhiteSpace(request.ShippingAddress.Country) ||
            string.IsNullOrWhiteSpace(request.ShippingAddress.ZipCode))
        {
            throw new OrderNotificationApiException(400, "A complete shippingAddress is required.");
        }
    }

    private static OrderNotificationApiException MapProviderException(
        MessagingProviderException exception,
        bool invalidDestinationIsBadRequest = false)
    {
        if (invalidDestinationIsBadRequest && exception.StatusCode is >= 400 and < 500 && exception.StatusCode is not 401 and not 403 and not 429)
        {
            return new OrderNotificationApiException(400, "The provider does not consider this a usable destination.");
        }
        return exception.StatusCode == 429
            ? new OrderNotificationApiException(503, "The messaging provider is temporarily unavailable.")
            : new OrderNotificationApiException(502, exception.Message);
    }
}
