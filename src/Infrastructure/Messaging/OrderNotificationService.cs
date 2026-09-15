using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public sealed class OrderNotificationService
{
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);
    private readonly CatalogContext _db;
    private readonly ITwilioMessagingClient _twilio;
    private readonly NotificationIdempotencyLock _idempotencyLock;

    public OrderNotificationService(
        CatalogContext db,
        ITwilioMessagingClient twilio,
        NotificationIdempotencyLock idempotencyLock)
    {
        _db = db;
        _twilio = twilio;
        _idempotencyLock = idempotencyLock;
    }

    public async Task<ContactNumberView> RegisterContactNumberAsync(
        string buyerId,
        string phoneNumber,
        string? countryCode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber) || phoneNumber.Length > 64)
        {
            throw new ContactNumberValidationException(new[] { "NOT_A_NUMBER" });
        }

        var validation = await _twilio.ValidatePhoneNumberAsync(phoneNumber.Trim(), countryCode, cancellationToken);
        if (!validation.IsValid || string.IsNullOrWhiteSpace(validation.CanonicalNumber))
        {
            throw new ContactNumberValidationException(validation.ValidationErrors);
        }

        var existing = await _db.ContactNumbers.SingleOrDefaultAsync(
            x => x.BuyerId == buyerId && x.CanonicalNumber == validation.CanonicalNumber,
            cancellationToken);
        if (existing is not null)
        {
            return ToView(existing);
        }

        var contact = new ContactNumber(buyerId, validation.CanonicalNumber, DateTimeOffset.UtcNow);
        _db.ContactNumbers.Add(contact);
        await _db.SaveChangesAsync(cancellationToken);
        return ToView(contact);
    }

    public async Task<IReadOnlyList<ContactNumberView>> GetContactNumbersAsync(string buyerId, CancellationToken cancellationToken)
    {
        return await _db.ContactNumbers
            .AsNoTracking()
            .Where(x => x.BuyerId == buyerId)
            .OrderBy(x => x.Id)
            .Select(x => new ContactNumberView(x.Id, x.CanonicalNumber, x.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<OperationResult> DeleteContactNumberAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken)
    {
        var contact = await _db.ContactNumbers.SingleOrDefaultAsync(
            x => x.Id == contactNumberId && x.BuyerId == buyerId,
            cancellationToken);
        if (contact is null)
        {
            return new OperationResult(OperationOutcome.NotFound);
        }

        var scheduled = await _db.OrderNotifications
            .Where(x => x.ContactNumberId == contactNumberId &&
                        x.ProviderMessageSid != null &&
                        (x.ProviderStatus == "scheduled" || x.ProviderStatus == NotificationProviderStatus.CancellationPending))
            .ToListAsync(cancellationToken);

        foreach (var notification in scheduled)
        {
            if (!await TryCancelScheduledMessageAsync(notification, cancellationToken))
            {
                await _db.SaveChangesAsync(cancellationToken);
                return new OperationResult(
                    OperationOutcome.ProviderUnavailable,
                    Error: "The number was not removed because a scheduled message could not yet be cancelled.");
            }
        }

        _db.ContactNumbers.Remove(contact);
        await _db.SaveChangesAsync(cancellationToken);
        return new OperationResult(OperationOutcome.Success);
    }

    public async Task<int> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<OrderLineInput> requestedLines,
        ShippingAddressInput address,
        CancellationToken cancellationToken)
    {
        if (requestedLines.Count == 0)
        {
            throw new OrderRequestValidationException("At least one catalog item is required.");
        }

        if (requestedLines.Any(x => x.CatalogItemId <= 0 || x.Quantity <= 0 || x.Quantity > 1000))
        {
            throw new OrderRequestValidationException("Catalog item ids and quantities must be positive; quantity cannot exceed 1000.");
        }

        ValidateAddress(address);
        List<OrderLineInput> lines;
        try
        {
            lines = requestedLines
                .GroupBy(x => x.CatalogItemId)
                .Select(x => new OrderLineInput(x.Key, checked(x.Sum(y => y.Quantity))))
                .ToList();
        }
        catch (OverflowException)
        {
            throw new OrderRequestValidationException("The total quantity is too large.");
        }

        if (lines.Any(x => x.Quantity > 1000))
        {
            throw new OrderRequestValidationException("The quantity for one catalog item cannot exceed 1000.");
        }
        var ids = lines.Select(x => x.CatalogItemId).ToArray();
        var catalogItems = await _db.CatalogItems
            .Where(x => ids.Contains(x.Id))
            .ToListAsync(cancellationToken);
        if (catalogItems.Count != ids.Length)
        {
            throw new OrderRequestValidationException("One or more catalog items do not exist.");
        }

        var orderItems = lines.Select(line =>
        {
            var catalogItem = catalogItems.Single(x => x.Id == line.CatalogItemId);
            return new OrderItem(
                new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri),
                catalogItem.Price,
                line.Quantity);
        }).ToList();
        var order = new Order(
            buyerId,
            new Address(address.Street, address.City, address.State, address.Country, address.ZipCode),
            orderItems);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);

        var contacts = await _db.ContactNumbers.Where(x => x.BuyerId == buyerId).ToListAsync(cancellationToken);
        foreach (var contact in contacts)
        {
            await CreateAndSubmitNotificationAsync(
                order,
                contact,
                NotificationKind.OrderPlaced,
                $"eShopOnWeb: Order {order.Id} was placed successfully.",
                null,
                null,
                null,
                cancellationToken);
        }

        return order.Id;
    }

    public async Task<OperationResult> DispatchOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        if (order is null)
        {
            return new OperationResult(OperationOutcome.NotFound);
        }

        bool changed;
        try
        {
            changed = order.Dispatch(DateTimeOffset.UtcNow);
        }
        catch (InvalidOperationException)
        {
            return new OperationResult(OperationOutcome.Conflict, Error: "A cancelled order cannot be dispatched.");
        }

        if (!changed)
        {
            return new OperationResult(OperationOutcome.Success, order.Id);
        }

        await _db.SaveChangesAsync(cancellationToken);
        var contacts = await _db.ContactNumbers.Where(x => x.BuyerId == order.BuyerId).ToListAsync(cancellationToken);
        foreach (var contact in contacts)
        {
            await CreateAndSubmitNotificationAsync(
                order,
                contact,
                NotificationKind.OrderDispatched,
                $"eShopOnWeb: Order {order.Id} is on its way.",
                null,
                null,
                null,
                cancellationToken);
            await CreateAndSubmitNotificationAsync(
                order,
                contact,
                NotificationKind.DeliveryFollowUp,
                $"eShopOnWeb: How did delivery of order {order.Id} go?",
                DateTimeOffset.UtcNow.Add(FollowUpDelay),
                null,
                null,
                cancellationToken);
        }

        return new OperationResult(OperationOutcome.Success, order.Id);
    }

    public async Task<OperationResult> CancelOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        if (order is null)
        {
            return new OperationResult(OperationOutcome.NotFound);
        }

        var changed = order.Cancel(DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);

        var scheduled = await _db.OrderNotifications
            .Where(x => x.OrderId == orderId &&
                        x.Kind == NotificationKind.DeliveryFollowUp &&
                        x.ProviderMessageSid != null &&
                        (x.ProviderStatus == "scheduled" || x.ProviderStatus == NotificationProviderStatus.CancellationPending))
            .ToListAsync(cancellationToken);
        foreach (var notification in scheduled)
        {
            await TryCancelScheduledMessageAsync(notification, cancellationToken);
        }
        await _db.SaveChangesAsync(cancellationToken);

        if (changed)
        {
            var contacts = await _db.ContactNumbers.Where(x => x.BuyerId == order.BuyerId).ToListAsync(cancellationToken);
            foreach (var contact in contacts)
            {
                await CreateAndSubmitNotificationAsync(
                    order,
                    contact,
                    NotificationKind.OrderCancelled,
                    $"eShopOnWeb: Order {order.Id} was cancelled.",
                    null,
                    null,
                    null,
                    cancellationToken);
            }
        }

        return new OperationResult(OperationOutcome.Success, order.Id);
    }

    public async Task<IReadOnlyList<OrderView>> GetOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        var orders = await _db.Orders
            .AsNoTracking()
            .Include(x => x.OrderItems)
            .ThenInclude(x => x.ItemOrdered)
            .Where(x => x.BuyerId == buyerId)
            .OrderByDescending(x => x.OrderDate)
            .ToListAsync(cancellationToken);
        var notifications = await _db.OrderNotifications
            .Where(x => x.BuyerId == buyerId)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        await RefreshProviderStatesAsync(notifications, cancellationToken);

        return orders.Select(order => new OrderView(
            order.Id,
            order.OrderDate,
            order.Status.ToString(),
            order.Total(),
            order.OrderItems.Select(x => new OrderItemView(
                x.ItemOrdered.CatalogItemId,
                x.ItemOrdered.ProductName,
                x.UnitPrice,
                x.Units)).ToList(),
            notifications.Where(x => x.OrderId == order.Id).Select(ToView).ToList())).ToList();
    }

    public async Task<(bool Found, IReadOnlyList<NotificationView> Notifications)> GetOrderNotificationsAsync(
        string buyerId,
        int orderId,
        CancellationToken cancellationToken)
    {
        var found = await _db.Orders.AnyAsync(x => x.Id == orderId && x.BuyerId == buyerId, cancellationToken);
        if (!found)
        {
            return (false, Array.Empty<NotificationView>());
        }

        var notifications = await _db.OrderNotifications
            .Where(x => x.OrderId == orderId && x.BuyerId == buyerId)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        await RefreshProviderStatesAsync(notifications, cancellationToken);
        return (true, notifications.Select(ToView).ToList());
    }

    public async Task<OperationResult> ResendNotificationAsync(
        int originalNotificationId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        idempotencyKey = idempotencyKey?.Trim() ?? string.Empty;
        if (idempotencyKey.Length is < 1 or > 128)
        {
            return new OperationResult(OperationOutcome.Conflict, Error: "The idempotency key must contain 1 to 128 characters.");
        }

        using var heldLock = await _idempotencyLock.AcquireAsync(originalNotificationId, idempotencyKey, cancellationToken);
        var existing = await _db.OrderNotifications.SingleOrDefaultAsync(
            x => x.OriginalNotificationId == originalNotificationId && x.ResendIdempotencyKey == idempotencyKey,
            cancellationToken);
        if (existing is not null)
        {
            return new OperationResult(OperationOutcome.Success, existing.Id);
        }

        var original = await _db.OrderNotifications.SingleOrDefaultAsync(x => x.Id == originalNotificationId, cancellationToken);
        if (original is null)
        {
            return new OperationResult(OperationOutcome.NotFound);
        }

        await RefreshProviderStatesAsync(new[] { original }, cancellationToken);
        if (!CanResend(original.ProviderStatus) || original.Body is null)
        {
            return new OperationResult(OperationOutcome.Conflict, Error: "Only a failed, undelivered message with retained content can be resent.");
        }

        var order = await _db.Orders.SingleOrDefaultAsync(x => x.Id == original.OrderId, cancellationToken);
        var contact = await _db.ContactNumbers.SingleOrDefaultAsync(x => x.Id == original.ContactNumberId, cancellationToken);
        if (order is null || contact is null || order.Status == OrderStatus.Cancelled && original.Kind == NotificationKind.DeliveryFollowUp)
        {
            return new OperationResult(OperationOutcome.Conflict, Error: "The destination or order is no longer eligible for this message.");
        }

        var resend = await CreateAndSubmitNotificationAsync(
            order,
            contact,
            NotificationKind.Resend,
            original.Body,
            null,
            original.Id,
            idempotencyKey,
            cancellationToken);
        return new OperationResult(OperationOutcome.Success, resend.Id);
    }

    public async Task<OperationResult> DisposeNotificationContentAsync(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _db.OrderNotifications.SingleOrDefaultAsync(x => x.Id == notificationId, cancellationToken);
        if (notification is null)
        {
            return new OperationResult(OperationOutcome.NotFound);
        }

        if (notification.ContentRedactedAt.HasValue)
        {
            return new OperationResult(OperationOutcome.Success, notification.Id);
        }

        if (!string.IsNullOrWhiteSpace(notification.ProviderMessageSid))
        {
            try
            {
                var provider = await _twilio.RedactMessageAsync(notification.ProviderMessageSid, cancellationToken);
                Apply(notification, provider);
            }
            catch (Exception ex) when (IsProviderFailure(ex))
            {
                return new OperationResult(
                    OperationOutcome.ProviderUnavailable,
                    Error: "Content was retained because the provider could not confirm redaction.");
            }
        }

        notification.Redact(DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);
        return new OperationResult(OperationOutcome.Success, notification.Id);
    }

    public async Task<IReadOnlyList<ReconciliationEntry>> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (to < from)
        {
            throw new OrderRequestValidationException("The 'to' date must be on or after 'from'.");
        }

        var providerMessages = await _twilio.ListMessagesAsync(from, to, cancellationToken);
        var localAll = await _db.OrderNotifications.ToListAsync(cancellationToken);
        var localBySid = localAll
            .Where(x => x.ProviderMessageSid != null)
            .ToDictionary(x => x.ProviderMessageSid!, StringComparer.Ordinal);
        var entries = new List<ReconciliationEntry>();

        foreach (var provider in providerMessages)
        {
            localBySid.TryGetValue(provider.Sid, out var local);
            if (local is not null)
            {
                Apply(local, provider);
            }

            entries.Add(new ReconciliationEntry(
                local is null ? "provider-only" : "matched",
                provider.Sid,
                local?.Id,
                provider.Status,
                local?.ProviderStatus,
                provider.ErrorCode,
                provider.DateSent,
                local?.CreatedAt));
        }

        var providerSids = providerMessages.Select(x => x.Sid).ToHashSet(StringComparer.Ordinal);
        entries.AddRange(localAll
            .Where(x => x.CreatedAt >= from && x.CreatedAt <= to &&
                        (x.ProviderMessageSid == null || !providerSids.Contains(x.ProviderMessageSid)))
            .Select(x => new ReconciliationEntry(
                "application-only",
                x.ProviderMessageSid,
                x.Id,
                null,
                x.ProviderStatus,
                x.ProviderErrorCode,
                x.ProviderDateSent,
                x.CreatedAt)));

        await _db.SaveChangesAsync(cancellationToken);
        return entries.OrderBy(x => x.ProviderDateSent ?? x.ApplicationCreatedAt).ToList();
    }

    public async Task RetryPendingCancellationsAsync(CancellationToken cancellationToken)
    {
        var pending = await _db.OrderNotifications
            .Where(x => x.ProviderStatus == NotificationProviderStatus.CancellationPending && x.ProviderMessageSid != null)
            .ToListAsync(cancellationToken);
        foreach (var notification in pending)
        {
            await TryCancelScheduledMessageAsync(notification, cancellationToken);
        }
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<OrderNotification> CreateAndSubmitNotificationAsync(
        Order order,
        ContactNumber contact,
        NotificationKind kind,
        string body,
        DateTimeOffset? scheduledFor,
        int? originalNotificationId,
        string? resendIdempotencyKey,
        CancellationToken cancellationToken)
    {
        var notification = new OrderNotification(
            order.Id,
            order.BuyerId,
            contact.Id,
            kind,
            body,
            DateTimeOffset.UtcNow,
            scheduledFor,
            originalNotificationId,
            resendIdempotencyKey);
        _db.OrderNotifications.Add(notification);
        await _db.SaveChangesAsync(cancellationToken);

        try
        {
            // Message creates are deliberately not retried. The provider has no idempotency key;
            // retrying an ambiguous timeout can create a duplicate text message.
            var provider = await _twilio.SendMessageAsync(contact.CanonicalNumber, body, scheduledFor, cancellationToken);
            notification.RecordProviderAcceptance(
                provider.Sid,
                provider.Status,
                provider.ErrorCode,
                provider.DateSent,
                DateTimeOffset.UtcNow);
        }
        catch (Exception ex) when (IsProviderFailure(ex))
        {
            notification.RecordSubmissionFailure((ex as TwilioApiException)?.ProviderErrorCode, DateTimeOffset.UtcNow);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return notification;
    }

    private async Task RefreshProviderStatesAsync(
        IEnumerable<OrderNotification> notifications,
        CancellationToken cancellationToken)
    {
        var changed = false;
        foreach (var notification in notifications.Where(x => x.ProviderMessageSid != null))
        {
            try
            {
                var provider = await _twilio.FetchMessageAsync(notification.ProviderMessageSid!, cancellationToken);
                Apply(notification, provider);
                changed = true;
            }
            catch (Exception ex) when (IsProviderFailure(ex))
            {
                // Status refresh is best effort; the last provider state remains reportable.
            }
        }

        if (changed)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<bool> TryCancelScheduledMessageAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                var provider = await _twilio.CancelMessageAsync(notification.ProviderMessageSid!, cancellationToken);
                Apply(notification, provider);
                return true;
            }
            catch (Exception ex) when (IsProviderFailure(ex))
            {
                if (attempt < 2)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(100 * (attempt + 1)), cancellationToken);
                }
            }
        }

        try
        {
            var current = await _twilio.FetchMessageAsync(notification.ProviderMessageSid!, cancellationToken);
            Apply(notification, current);
            if (!string.Equals(current.Status, "scheduled", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        catch (Exception ex) when (IsProviderFailure(ex))
        {
        }

        notification.MarkCancellationPending(DateTimeOffset.UtcNow);
        return false;
    }

    private static bool CanResend(string status) =>
        status is "failed" or "undelivered" or NotificationProviderStatus.SubmissionFailed;

    private static void Apply(OrderNotification notification, ProviderMessage provider) =>
        notification.ApplyProviderState(provider.Status, provider.ErrorCode, provider.DateSent, DateTimeOffset.UtcNow);

    private static bool IsProviderFailure(Exception exception) =>
        exception is TwilioApiException or HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException;

    private static ContactNumberView ToView(ContactNumber number) => new(number.Id, number.CanonicalNumber, number.CreatedAt);

    private static NotificationView ToView(OrderNotification notification) => new(
        notification.Id,
        notification.OrderId,
        notification.Kind.ToString(),
        notification.Body,
        notification.ProviderStatus,
        notification.ProviderMessageSid,
        notification.ProviderErrorCode,
        notification.CreatedAt,
        notification.ScheduledFor,
        notification.ProviderDateSent,
        notification.ContentRedactedAt,
        notification.OriginalNotificationId);

    private static void ValidateAddress(ShippingAddressInput address)
    {
        if (address is null ||
            string.IsNullOrWhiteSpace(address.Street) ||
            string.IsNullOrWhiteSpace(address.City) ||
            string.IsNullOrWhiteSpace(address.Country) ||
            string.IsNullOrWhiteSpace(address.ZipCode))
        {
            throw new OrderRequestValidationException("A complete shipping address is required.");
        }
    }
}
