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

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public sealed class OrderNotificationService(CatalogContext context, IMessageProvider provider) : IOrderNotificationService
{
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    public async Task<ContactNumberDto> RegisterContactNumberAsync(
        string buyerId,
        string phoneNumber,
        CancellationToken cancellationToken)
    {
        RequireBuyer(buyerId);
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw new NotificationValidationException("phoneNumber is required.");
        }

        string canonicalNumber = await provider.ValidateAndCanonicalizeAsync(phoneNumber, cancellationToken);
        bool duplicate = await context.ContactNumbers.AnyAsync(
            x => x.BuyerId == buyerId && x.CanonicalNumber == canonicalNumber && x.RemovedAt == null,
            cancellationToken);
        if (duplicate)
        {
            throw new NotificationConflictException("That contact number is already registered.");
        }

        var contactNumber = new ContactNumber(buyerId, canonicalNumber, DateTimeOffset.UtcNow);
        context.ContactNumbers.Add(contactNumber);
        await context.SaveChangesAsync(cancellationToken);
        return Map(contactNumber);
    }

    public async Task<IReadOnlyList<ContactNumberDto>> GetContactNumbersAsync(
        string buyerId,
        CancellationToken cancellationToken)
    {
        RequireBuyer(buyerId);
        return await context.ContactNumbers.AsNoTracking()
            .Where(x => x.BuyerId == buyerId && x.RemovedAt == null)
            .OrderBy(x => x.Id)
            .Select(x => new ContactNumberDto(x.Id, x.CanonicalNumber, x.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> DeleteContactNumberAsync(
        string buyerId,
        int contactNumberId,
        CancellationToken cancellationToken)
    {
        RequireBuyer(buyerId);
        ContactNumber? contactNumber = await context.ContactNumbers.SingleOrDefaultAsync(
            x => x.Id == contactNumberId && x.BuyerId == buyerId && x.RemovedAt == null,
            cancellationToken);
        if (contactNumber is null)
        {
            return false;
        }

        contactNumber.Remove(DateTimeOffset.UtcNow);
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<int> PlaceOrderAsync(
        string buyerId,
        PlaceOrderCommand command,
        CancellationToken cancellationToken)
    {
        RequireBuyer(buyerId);
        ValidateOrder(command);

        var requested = command.Items
            .GroupBy(x => x.CatalogItemId)
            .ToDictionary(x => x.Key, x => x.Sum(item => item.Quantity));
        List<int> itemIds = requested.Keys.ToList();
        List<Microsoft.eShopWeb.ApplicationCore.Entities.CatalogItem> catalogItems = await context.CatalogItems
            .Where(x => itemIds.Contains(x.Id))
            .ToListAsync(cancellationToken);
        if (catalogItems.Count != itemIds.Count)
        {
            throw new NotificationValidationException("One or more catalogItemIds do not exist.");
        }

        List<OrderItem> orderItems = catalogItems.Select(item => new OrderItem(
            new CatalogItemOrdered(item.Id, item.Name, item.PictureUri),
            item.Price,
            requested[item.Id])).ToList();
        var address = new Address(
            command.ShippingAddress.Street,
            command.ShippingAddress.City,
            command.ShippingAddress.State,
            command.ShippingAddress.Country,
            command.ShippingAddress.ZipCode);
        var order = new Order(buyerId, address, orderItems);
        context.Orders.Add(order);
        await context.SaveChangesAsync(cancellationToken);

        await NotifyActiveNumbersAsync(
            order,
            NotificationKind.OrderPlaced,
            $"Your eShop order #{order.Id} was placed successfully.",
            false,
            null,
            CancellationToken.None);
        return order.Id;
    }

    public async Task DispatchOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        Order order = await FindOrderAsync(orderId, cancellationToken);
        try
        {
            order.Dispatch(DateTimeOffset.UtcNow);
        }
        catch (InvalidOperationException ex)
        {
            throw new NotificationConflictException(ex.Message);
        }

        await context.SaveChangesAsync(cancellationToken);
        await NotifyActiveNumbersAsync(
            order,
            NotificationKind.OrderDispatched,
            $"Your eShop order #{order.Id} is on its way.",
            false,
            null,
            CancellationToken.None);

        DateTimeOffset sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        await NotifyActiveNumbersAsync(
            order,
            NotificationKind.DeliveryFollowUp,
            $"How did delivery of eShop order #{order.Id} go? We would love your feedback.",
            true,
            sendAt,
            CancellationToken.None);
    }

    public async Task CancelOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        Order order = await FindOrderAsync(orderId, cancellationToken);
        if (order.Status == OrderStatus.Cancelled)
        {
            return;
        }

        order.Cancel(DateTimeOffset.UtcNow);
        await context.SaveChangesAsync(cancellationToken);

        List<OrderNotification> scheduled = await context.OrderNotifications
            .Where(x => x.OrderId == orderId && x.Kind == NotificationKind.DeliveryFollowUp &&
                x.ProviderSid != null && x.ProviderStatus != "canceled" && x.ProviderStatus != "delivered")
            .ToListAsync(CancellationToken.None);
        foreach (OrderNotification notification in scheduled)
        {
            try
            {
                ProviderMessage state = await provider.CancelAsync(notification.ProviderSid!, CancellationToken.None);
                Apply(notification, state);
            }
            catch (MessageProviderException)
            {
                notification.MarkProviderFailure(DateTimeOffset.UtcNow);
            }

            await context.SaveChangesAsync(CancellationToken.None);
        }

        await NotifyActiveNumbersAsync(
            order,
            NotificationKind.OrderCancelled,
            $"Your eShop order #{order.Id} was cancelled.",
            false,
            null,
            CancellationToken.None);
    }

    public async Task<IReadOnlyList<OrderDto>> GetMyOrdersAsync(
        string buyerId,
        CancellationToken cancellationToken)
    {
        RequireBuyer(buyerId);
        List<Order> orders = await context.Orders.AsNoTracking()
            .Where(x => x.BuyerId == buyerId)
            .Include(x => x.OrderItems)
            .ThenInclude(x => x.ItemOrdered)
            .OrderByDescending(x => x.OrderDate)
            .ToListAsync(cancellationToken);
        List<OrderNotification> notifications = await context.OrderNotifications
            .Where(x => x.BuyerId == buyerId)
            .ToListAsync(cancellationToken);
        await RefreshAsync(notifications, cancellationToken);

        return orders.Select(order =>
        {
            List<OrderNotification> orderNotifications = notifications.Where(x => x.OrderId == order.Id).ToList();
            return new OrderDto(
                order.Id,
                order.OrderDate,
                order.Status.ToString(),
                order.Total(),
                order.OrderItems.Select(item => new OrderLineDto(
                    item.ItemOrdered.CatalogItemId,
                    item.ItemOrdered.ProductName,
                    item.UnitPrice,
                    item.Units)).ToList(),
                Summarize(orderNotifications));
        }).ToList();
    }

    public async Task<IReadOnlyList<NotificationDto>?> GetOrderNotificationsAsync(
        string buyerId,
        int orderId,
        CancellationToken cancellationToken)
    {
        RequireBuyer(buyerId);
        bool ownsOrder = await context.Orders.AsNoTracking()
            .AnyAsync(x => x.Id == orderId && x.BuyerId == buyerId, cancellationToken);
        if (!ownsOrder)
        {
            return null;
        }

        List<OrderNotification> notifications = await context.OrderNotifications
            .Where(x => x.OrderId == orderId && x.BuyerId == buyerId)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
        await RefreshAsync(notifications, cancellationToken);
        return notifications.Select(Map).ToList();
    }

    public async Task<int> ResendAsync(
        int notificationId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 200)
        {
            throw new NotificationValidationException("idempotencyKey is required and must be at most 200 characters.");
        }

        string key = idempotencyKey.Trim();
        NotificationResend? existing = await context.NotificationResends.AsNoTracking()
            .SingleOrDefaultAsync(x => x.IdempotencyKey == key, cancellationToken);
        if (existing is not null)
        {
            if (existing.OriginalNotificationId != notificationId)
            {
                throw new NotificationConflictException("The idempotency key was already used for another notification.");
            }

            if (existing.NewNotificationId is { } completedId)
            {
                return completedId;
            }

            throw new NotificationConflictException("A resend with this idempotency key is already in progress.");
        }

        OrderNotification original = await context.OrderNotifications
            .SingleOrDefaultAsync(x => x.Id == notificationId, cancellationToken)
            ?? throw new NotificationResourceNotFoundException("Notification not found.");
        if (!CanResend(original))
        {
            throw new NotificationConflictException("Only a message that did not reach the shopper can be resent.");
        }
        if (string.IsNullOrEmpty(original.Body))
        {
            throw new NotificationConflictException("Disposed message content cannot be resent.");
        }

        ContactNumber contactNumber = await context.ContactNumbers.SingleOrDefaultAsync(
            x => x.Id == original.ContactNumberId && x.RemovedAt == null,
            cancellationToken) ?? throw new NotificationConflictException("The destination is no longer registered.");

        var resendReservation = new NotificationResend(key, notificationId, DateTimeOffset.UtcNow);
        context.NotificationResends.Add(resendReservation);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            context.Entry(resendReservation).State = EntityState.Detached;
            NotificationResend winner = await context.NotificationResends.AsNoTracking()
                .SingleAsync(x => x.IdempotencyKey == key, cancellationToken);
            if (winner.OriginalNotificationId == notificationId && winner.NewNotificationId is { } winnerId)
            {
                return winnerId;
            }
            throw new NotificationConflictException("A resend with this idempotency key already exists.");
        }

        var resend = new OrderNotification(
            original.OrderId,
            original.BuyerId,
            original.ContactNumberId,
            NotificationKind.Resend,
            original.Body,
            DateTimeOffset.UtcNow,
            sourceNotificationId: original.Id);
        context.OrderNotifications.Add(resend);
        await context.SaveChangesAsync(cancellationToken);
        resendReservation.Complete(resend.Id);
        await context.SaveChangesAsync(cancellationToken);

        bool stillActive = await context.ContactNumbers.AsNoTracking()
            .AnyAsync(x => x.Id == contactNumber.Id && x.RemovedAt == null, CancellationToken.None);
        if (!stillActive)
        {
            resend.MarkProviderFailure(DateTimeOffset.UtcNow);
            await context.SaveChangesAsync(CancellationToken.None);
            return resend.Id;
        }

        await SubmitAsync(resend, contactNumber.CanonicalNumber, false, null, CancellationToken.None);
        return resend.Id;
    }

    public async Task DisposeContentAsync(int notificationId, CancellationToken cancellationToken)
    {
        OrderNotification notification = await context.OrderNotifications
            .SingleOrDefaultAsync(x => x.Id == notificationId, cancellationToken)
            ?? throw new NotificationResourceNotFoundException("Notification not found.");
        if (notification.ContentDisposedAt is not null)
        {
            return;
        }

        if (notification.ProviderSid is not null)
        {
            ProviderMessage state = await provider.DisposeContentAsync(notification.ProviderSid, cancellationToken);
            Apply(notification, state);
        }

        notification.MarkContentDisposed(DateTimeOffset.UtcNow);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<ReconciliationDto> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (from > to)
        {
            throw new NotificationValidationException("from must be earlier than or equal to to.");
        }

        IReadOnlyList<ProviderMessage> providerMessages = await provider.ListAsync(from, to, cancellationToken);
        List<string> providerSids = providerMessages.Select(x => x.Sid).ToList();
        List<OrderNotification> local = await context.OrderNotifications.AsNoTracking()
            .Where(x => (x.CreatedAt >= from && x.CreatedAt <= to) ||
                (x.ProviderSid != null && providerSids.Contains(x.ProviderSid)))
            .ToListAsync(cancellationToken);
        var localBySid = local.Where(x => x.ProviderSid is not null)
            .ToDictionary(x => x.ProviderSid!, StringComparer.Ordinal);
        var entries = new List<ReconciliationEntryDto>();
        var matchedLocalIds = new HashSet<int>();

        foreach (ProviderMessage message in providerMessages)
        {
            if (localBySid.TryGetValue(message.Sid, out OrderNotification? notification))
            {
                matchedLocalIds.Add(notification.Id);
                entries.Add(new ReconciliationEntryDto(
                    "matched", message.Sid, notification.Id, message.Status, notification.ProviderStatus,
                    message.DateSent, notification.CreatedAt));
            }
            else
            {
                entries.Add(new ReconciliationEntryDto(
                    "providerOnly", message.Sid, null, message.Status, null, message.DateSent, null));
            }
        }

        foreach (OrderNotification notification in local.Where(x => x.CreatedAt >= from && x.CreatedAt <= to && !matchedLocalIds.Contains(x.Id)))
        {
            entries.Add(new ReconciliationEntryDto(
                "applicationOnly", notification.ProviderSid, notification.Id, null,
                notification.ProviderStatus, null, notification.CreatedAt));
        }

        entries = entries.OrderBy(x => x.ProviderDateSent ?? x.ApplicationCreatedAt).ToList();
        return new ReconciliationDto(
            from,
            to,
            entries,
            entries.Count(x => x.Alignment == "matched"),
            entries.Count(x => x.Alignment == "providerOnly"),
            entries.Count(x => x.Alignment == "applicationOnly"));
    }

    private async Task NotifyActiveNumbersAsync(
        Order order,
        NotificationKind kind,
        string body,
        bool scheduled,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        List<ContactNumber> contactNumbers = await context.ContactNumbers.AsNoTracking()
            .Where(x => x.BuyerId == order.BuyerId && x.RemovedAt == null)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        foreach (ContactNumber contactNumber in contactNumbers)
        {
            var notification = new OrderNotification(
                order.Id,
                order.BuyerId,
                contactNumber.Id,
                kind,
                body,
                DateTimeOffset.UtcNow,
                scheduled,
                sendAt);
            context.OrderNotifications.Add(notification);
            await context.SaveChangesAsync(cancellationToken);
            await SubmitAsync(notification, contactNumber.CanonicalNumber, scheduled, sendAt, cancellationToken);
        }
    }

    private async Task SubmitAsync(
        OrderNotification notification,
        string canonicalNumber,
        bool scheduled,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        try
        {
            ProviderMessage state = scheduled
                ? await provider.ScheduleAsync(canonicalNumber, notification.Body!, sendAt!.Value, cancellationToken)
                : await provider.SendAsync(canonicalNumber, notification.Body!, cancellationToken);
            Apply(notification, state);
        }
        catch (MessageProviderException)
        {
            notification.MarkProviderFailure(DateTimeOffset.UtcNow);
        }

        await context.SaveChangesAsync(CancellationToken.None);
    }

    private async Task RefreshAsync(List<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        bool changed = false;
        foreach (OrderNotification notification in notifications.Where(x => x.ProviderSid is not null))
        {
            try
            {
                ProviderMessage state = await provider.GetAsync(notification.ProviderSid!, cancellationToken);
                Apply(notification, state);
                changed = true;
            }
            catch (MessageProviderException)
            {
                // A read remains available with the last persisted provider state.
            }
        }

        if (changed)
        {
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<Order> FindOrderAsync(int orderId, CancellationToken cancellationToken) =>
        await context.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken)
            ?? throw new NotificationResourceNotFoundException("Order not found.");

    private static void Apply(OrderNotification notification, ProviderMessage state) =>
        notification.ApplyProviderState(
            state.Sid,
            state.Status,
            state.ErrorCode,
            state.DateCreated,
            state.DateSent,
            state.DateUpdated,
            DateTimeOffset.UtcNow);

    private static ContactNumberDto Map(ContactNumber contactNumber) =>
        new(contactNumber.Id, contactNumber.CanonicalNumber, contactNumber.CreatedAt);

    private static NotificationDto Map(OrderNotification notification) =>
        new(
            notification.Id,
            notification.OrderId,
            notification.Kind.ToString(),
            notification.Body,
            notification.ProviderStatus,
            notification.ProviderSid,
            notification.ProviderErrorCode,
            notification.IsScheduled,
            notification.ScheduledFor,
            notification.CreatedAt,
            notification.ProviderDateSent,
            notification.ContentDisposedAt,
            notification.SourceNotificationId);

    private static NotificationSummaryDto Summarize(IReadOnlyCollection<OrderNotification> notifications) =>
        new(
            notifications.Count,
            notifications.Count(x => x.ProviderStatus == "delivered"),
            notifications.Count(x => x.ProviderStatus is "failed" or "undelivered" or "provider_error"),
            notifications.Count(x => x.ProviderStatus is "pending" or "accepted" or "queued" or "sending" or "sent"),
            notifications.Count(x => x.ProviderStatus == "scheduled"),
            notifications.Count(x => x.ProviderStatus == "canceled"));

    private static bool CanResend(OrderNotification notification) =>
        notification.Kind != NotificationKind.DeliveryFollowUp &&
        notification.ProviderStatus is "failed" or "undelivered" or "provider_error";

    private static void RequireBuyer(string buyerId)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new NotificationValidationException("The authenticated user identity is missing.");
        }
    }

    private static void ValidateOrder(PlaceOrderCommand command)
    {
        if (command.Items is null || command.Items.Count == 0)
        {
            throw new NotificationValidationException("At least one order item is required.");
        }
        if (command.Items.Any(x => x.CatalogItemId <= 0 || x.Quantity <= 0))
        {
            throw new NotificationValidationException("Catalog item ids and quantities must be positive.");
        }
        if (command.ShippingAddress is null ||
            new[]
            {
                command.ShippingAddress.Street,
                command.ShippingAddress.City,
                command.ShippingAddress.State,
                command.ShippingAddress.Country,
                command.ShippingAddress.ZipCode
            }.Any(string.IsNullOrWhiteSpace))
        {
            throw new NotificationValidationException("A complete shippingAddress is required.");
        }
    }
}
