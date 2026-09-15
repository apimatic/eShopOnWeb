using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public sealed class OrderNotificationService(CatalogContext context, IMessagingProvider provider) : IOrderNotificationService
{
    private static readonly SemaphoreSlim ResendLock = new(1, 1);
    private static readonly TimeSpan ProviderBudget = TimeSpan.FromSeconds(30);

    public async Task<ContactNumberResult> RegisterContactAsync(
        string buyerId,
        string input,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            throw new ApiOperationException(400, "A mobile number is required.");
        }

        DestinationValidation validation;
        try
        {
            validation = await provider.ValidateDestinationAsync(input, cancellationToken);
        }
        catch (MessagingProviderException ex)
        {
            throw ToApiException(ex, "The mobile number could not be validated.");
        }

        if (!validation.IsValid || string.IsNullOrWhiteSpace(validation.CanonicalNumber))
        {
            throw new ApiOperationException(400, "The provider does not consider that number a usable destination.");
        }

        var existing = await context.ContactNumbers
            .SingleOrDefaultAsync(
                x => x.BuyerId == buyerId && x.CanonicalNumber == validation.CanonicalNumber && x.IsActive,
                cancellationToken);
        if (existing is not null)
        {
            return Map(existing);
        }

        var contact = new ContactNumber(buyerId, validation.CanonicalNumber, DateTimeOffset.UtcNow);
        context.ContactNumbers.Add(contact);
        await context.SaveChangesAsync(cancellationToken);
        return Map(contact);
    }

    public async Task<IReadOnlyList<ContactNumberResult>> GetContactsAsync(string buyerId, CancellationToken cancellationToken) =>
        await context.ContactNumbers.AsNoTracking()
            .Where(x => x.BuyerId == buyerId && x.IsActive)
            .OrderBy(x => x.Id)
            .Select(x => new ContactNumberResult(x.Id, x.CanonicalNumber, x.CreatedAt))
            .ToListAsync(cancellationToken);

    public async Task<bool> RemoveContactAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken)
    {
        var contact = await context.ContactNumbers
            .SingleOrDefaultAsync(x => x.Id == contactNumberId && x.BuyerId == buyerId && x.IsActive, cancellationToken);
        if (contact is null)
        {
            return false;
        }

        contact.Remove(DateTimeOffset.UtcNow);
        await context.SaveChangesAsync(cancellationToken);

        var scheduled = await context.OrderNotifications
            .Where(x => x.ContactNumberId == contact.Id &&
                        x.Kind == NotificationKind.DeliveryFollowUp &&
                        x.ProviderMessageSid != null &&
                        x.ProviderStatus != "canceled" &&
                        x.ProviderStatus != "delivered" &&
                        x.ProviderStatus != "sent")
            .ToListAsync(cancellationToken);
        await CancelScheduledBestEffortAsync(scheduled, cancellationToken);
        return true;
    }

    public async Task<int> PlaceOrderAsync(string buyerId, PlaceOrderCommand command, CancellationToken cancellationToken)
    {
        if (command.Items.Count == 0 || command.Items.Any(x => x.CatalogItemId <= 0 || x.Quantity <= 0))
        {
            throw new ApiOperationException(400, "At least one catalog item with a positive quantity is required.");
        }

        var requested = command.Items
            .GroupBy(x => x.CatalogItemId)
            .ToDictionary(x => x.Key, x => x.Sum(line => line.Quantity));
        var catalogItems = await context.CatalogItems
            .Where(x => requested.Keys.Contains(x.Id))
            .ToListAsync(cancellationToken);
        if (catalogItems.Count != requested.Count)
        {
            throw new ApiOperationException(400, "One or more catalog items do not exist.");
        }

        var orderItems = catalogItems.Select(item => new OrderItem(
            new CatalogItemOrdered(item.Id, item.Name, item.PictureUri),
            item.Price,
            requested[item.Id])).ToList();
        var address = command.ShippingAddress;
        if (string.IsNullOrWhiteSpace(address.Street) ||
            string.IsNullOrWhiteSpace(address.City) ||
            string.IsNullOrWhiteSpace(address.Country) ||
            string.IsNullOrWhiteSpace(address.ZipCode))
        {
            throw new ApiOperationException(400, "A complete shipping address is required.");
        }

        var order = new Order(
            buyerId,
            new Address(address.Street, address.City, address.State, address.Country, address.ZipCode),
            orderItems);
        context.Orders.Add(order);
        await context.SaveChangesAsync(cancellationToken);

        await NotifyActiveContactsAsync(
            order,
            NotificationKind.OrderPlaced,
            $"Your order #{order.Id} has been placed.",
            sendAt: null,
            cancellationToken);
        return order.Id;
    }

    public async Task<bool> DispatchOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await context.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        if (order is null)
        {
            return false;
        }

        try
        {
            order.Dispatch(DateTimeOffset.UtcNow);
        }
        catch (InvalidOperationException ex)
        {
            throw new ApiOperationException(409, ex.Message);
        }

        await context.SaveChangesAsync(cancellationToken);
        await NotifyActiveContactsAsync(
            order,
            NotificationKind.OrderDispatched,
            $"Your order #{order.Id} has been dispatched and is on its way.",
            sendAt: null,
            cancellationToken);
        await NotifyActiveContactsAsync(
            order,
            NotificationKind.DeliveryFollowUp,
            $"How did delivery of order #{order.Id} go? We would love your feedback.",
            DateTimeOffset.UtcNow.AddDays(3),
            cancellationToken);
        return true;
    }

    public async Task<bool> CancelOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await context.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        if (order is null)
        {
            return false;
        }

        var wasAlreadyCancelled = order.Status == OrderStatus.Cancelled;
        order.Cancel(DateTimeOffset.UtcNow);
        await context.SaveChangesAsync(cancellationToken);

        var scheduled = await context.OrderNotifications
            .Where(x => x.OrderId == order.Id &&
                        x.Kind == NotificationKind.DeliveryFollowUp &&
                        x.ProviderMessageSid != null &&
                        x.ProviderStatus != "canceled" &&
                        x.ProviderStatus != "delivered" &&
                        x.ProviderStatus != "sent")
            .ToListAsync(cancellationToken);
        await CancelScheduledBestEffortAsync(scheduled, cancellationToken);

        if (!wasAlreadyCancelled)
        {
            await NotifyActiveContactsAsync(
                order,
                NotificationKind.OrderCancelled,
                $"Your order #{order.Id} has been cancelled.",
                sendAt: null,
                cancellationToken);
        }

        return true;
    }

    public async Task<IReadOnlyList<OrderSummaryResult>> GetOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        var orders = await context.Orders.AsNoTracking()
            .Include(x => x.OrderItems)
            .Where(x => x.BuyerId == buyerId)
            .OrderByDescending(x => x.OrderDate)
            .ToListAsync(cancellationToken);
        var orderIds = orders.Select(x => x.Id).ToArray();
        var notifications = await context.OrderNotifications
            .Where(x => orderIds.Contains(x.OrderId))
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
        await RefreshBestEffortAsync(notifications, cancellationToken);

        return orders.Select(order => new OrderSummaryResult(
            order.Id,
            order.Status.ToString(),
            order.OrderDate,
            order.Total(),
            notifications.Where(x => x.OrderId == order.Id).Select(Map).ToList())).ToList();
    }

    public async Task<IReadOnlyList<NotificationResult>?> GetNotificationsAsync(
        string buyerId,
        int orderId,
        CancellationToken cancellationToken)
    {
        if (!await context.Orders.AnyAsync(x => x.Id == orderId && x.BuyerId == buyerId, cancellationToken))
        {
            return null;
        }

        var notifications = await context.OrderNotifications
            .Where(x => x.OrderId == orderId && x.BuyerId == buyerId)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
        await RefreshBestEffortAsync(notifications, cancellationToken);
        return notifications.Select(Map).ToList();
    }

    public async Task<int?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 200)
        {
            throw new ApiOperationException(400, "An idempotency key of at most 200 characters is required.");
        }

        await ResendLock.WaitAsync(cancellationToken);
        try
        {
            var priorAttempt = await context.OrderNotifications
                .SingleOrDefaultAsync(
                    x => x.ResendOfNotificationId == notificationId && x.IdempotencyKey == idempotencyKey,
                    cancellationToken);
            if (priorAttempt is not null)
            {
                return priorAttempt.Id;
            }

            var original = await context.OrderNotifications.SingleOrDefaultAsync(x => x.Id == notificationId, cancellationToken);
            if (original is null)
            {
                return null;
            }

            var contact = await context.ContactNumbers.SingleOrDefaultAsync(
                x => x.Id == original.ContactNumberId && x.IsActive,
                cancellationToken);
            if (contact is null)
            {
                throw new ApiOperationException(409, "The destination is no longer registered and cannot be messaged.");
            }

            if (original.Body is null)
            {
                throw new ApiOperationException(409, "Disposed message content cannot be resent.");
            }

            if (original.ProviderMessageSid is not null)
            {
                try
                {
                    original.RecordProviderResult(
                        await provider.FetchAsync(original.ProviderMessageSid, cancellationToken),
                        DateTimeOffset.UtcNow);
                    await context.SaveChangesAsync(cancellationToken);
                }
                catch (MessagingProviderException ex)
                {
                    throw ToApiException(ex, "The message outcome could not be confirmed.");
                }
            }

            if (!IsResendEligible(original.ProviderStatus))
            {
                throw new ApiOperationException(409, "Only a message confirmed failed or undelivered can be resent.");
            }

            var resend = new OrderNotification(
                original.OrderId,
                original.BuyerId,
                original.ContactNumberId,
                NotificationKind.Resend,
                original.Body,
                DateTimeOffset.UtcNow,
                resendOfNotificationId: original.Id,
                idempotencyKey: idempotencyKey);
            context.OrderNotifications.Add(resend);
            await context.SaveChangesAsync(cancellationToken);
            await SendBestEffortAsync(resend, contact.CanonicalNumber, sendAt: null, cancellationToken);
            return resend.Id;
        }
        finally
        {
            ResendLock.Release();
        }
    }

    public async Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await context.OrderNotifications.SingleOrDefaultAsync(x => x.Id == notificationId, cancellationToken);
        if (notification is null)
        {
            return false;
        }

        if (notification.ContentDisposedAt.HasValue)
        {
            return true;
        }

        if (notification.ProviderMessageSid is not null)
        {
            try
            {
                notification.RecordProviderResult(
                    await provider.DisposeContentAsync(notification.ProviderMessageSid, cancellationToken),
                    DateTimeOffset.UtcNow);
            }
            catch (MessagingProviderException ex)
            {
                throw ToApiException(ex, "The provider could not dispose of the message content.");
            }
        }

        notification.DisposeContent(DateTimeOffset.UtcNow);
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<ReconciliationResult> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (from > to)
        {
            throw new ApiOperationException(400, "The from instant must not be later than to.");
        }

        IReadOnlyList<ProviderMessageState> providerMessages;
        try
        {
            providerMessages = await provider.ListSentAsync(from, to, cancellationToken);
        }
        catch (MessagingProviderException ex)
        {
            throw ToApiException(ex, "Reconciliation could not be completed.");
        }

        var local = await context.OrderNotifications.AsNoTracking()
            .Where(x =>
                (x.ProviderSentAt >= from && x.ProviderSentAt <= to) ||
                (x.ProviderSentAt == null && (x.AttemptedAt ?? x.CreatedAt) >= from && (x.AttemptedAt ?? x.CreatedAt) <= to))
            .ToListAsync(cancellationToken);
        var localBySid = local.Where(x => x.ProviderMessageSid != null)
            .GroupBy(x => x.ProviderMessageSid!, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
        var entries = new List<ReconciliationEntry>();
        var matchedLocalIds = new HashSet<int>();

        foreach (var providerMessage in providerMessages)
        {
            if (providerMessage.Sid is not null && localBySid.TryGetValue(providerMessage.Sid, out var notification))
            {
                matchedLocalIds.Add(notification.Id);
                entries.Add(new ReconciliationEntry(
                    providerMessage.Sid,
                    notification.Id,
                    "matched",
                    providerMessage.Status,
                    notification.ProviderStatus,
                    providerMessage.DateSent));
            }
            else
            {
                entries.Add(new ReconciliationEntry(
                    providerMessage.Sid,
                    null,
                    "provider_only",
                    providerMessage.Status,
                    null,
                    providerMessage.DateSent));
            }
        }

        entries.AddRange(local.Where(x => !matchedLocalIds.Contains(x.Id)).Select(x => new ReconciliationEntry(
            x.ProviderMessageSid,
            x.Id,
            "local_only",
            null,
            x.ProviderStatus,
            x.ProviderDateSent)));
        return new ReconciliationResult(from, to, entries);
    }

    private async Task NotifyActiveContactsAsync(
        Order order,
        NotificationKind kind,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        var contacts = await context.ContactNumbers
            .Where(x => x.BuyerId == order.BuyerId && x.IsActive)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        foreach (var contact in contacts)
        {
            var notification = new OrderNotification(
                order.Id,
                order.BuyerId,
                contact.Id,
                kind,
                body,
                DateTimeOffset.UtcNow,
                sendAt);
            context.OrderNotifications.Add(notification);
            await context.SaveChangesAsync(cancellationToken);
            await SendBestEffortAsync(notification, contact.CanonicalNumber, sendAt, cancellationToken);
        }
    }

    private async Task SendBestEffortAsync(
        OrderNotification notification,
        string destination,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        try
        {
            notification.RecordProviderResult(
                await provider.SendAsync(destination, notification.Body!, sendAt, cancellationToken),
                DateTimeOffset.UtcNow);
        }
        catch (MessagingProviderException ex)
        {
            var rejected = ex.StatusCode is >= 400 and < 500;
            notification.RecordFailure(rejected ? "provider_rejected" : "outcome_unknown", ex.Message, DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException)
        {
            notification.RecordFailure("outcome_unknown", "The notification attempt was interrupted.", DateTimeOffset.UtcNow);
        }

        await context.SaveChangesAsync(CancellationToken.None);
    }

    private async Task CancelScheduledBestEffortAsync(
        IReadOnlyList<OrderNotification> notifications,
        CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            try
            {
                notification.RecordProviderResult(
                    await provider.CancelScheduledAsync(notification.ProviderMessageSid!, cancellationToken),
                    DateTimeOffset.UtcNow);
            }
            catch (MessagingProviderException ex)
            {
                notification.RecordFailure("cancellation_pending", ex.Message, DateTimeOffset.UtcNow);
            }
            catch (OperationCanceledException)
            {
                notification.RecordFailure("cancellation_pending", "The cancellation attempt was interrupted.", DateTimeOffset.UtcNow);
            }

            await context.SaveChangesAsync(CancellationToken.None);
        }
    }

    private async Task RefreshBestEffortAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(ProviderBudget);
        foreach (var notification in notifications.Where(x => x.ProviderMessageSid is not null))
        {
            try
            {
                notification.RecordProviderResult(
                    await provider.FetchAsync(notification.ProviderMessageSid!, budget.Token),
                    DateTimeOffset.UtcNow);
            }
            catch (MessagingProviderException)
            {
                // Reads retain and return the durable last-known provider outcome.
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        await context.SaveChangesAsync(CancellationToken.None);
    }

    private static bool IsResendEligible(string status) =>
        string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "undelivered", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "provider_rejected", StringComparison.OrdinalIgnoreCase);

    private static ContactNumberResult Map(ContactNumber contact) =>
        new(contact.Id, contact.CanonicalNumber, contact.CreatedAt);

    private static NotificationResult Map(OrderNotification notification) => new(
        notification.Id,
        notification.OrderId,
        notification.Kind.ToString(),
        notification.Body,
        notification.ProviderStatus,
        notification.ProviderMessageSid,
        notification.ProviderErrorCode,
        notification.ProviderErrorMessage,
        notification.CreatedAt,
        notification.ScheduledFor,
        notification.ContentDisposedAt,
        notification.ResendOfNotificationId);

    private static ApiOperationException ToApiException(MessagingProviderException ex, string fallback)
    {
        var status = ex.StatusCode switch
        {
            >= 400 and < 500 => ex.StatusCode.Value,
            503 => 503,
            _ => 502
        };
        return new ApiOperationException(status, string.IsNullOrWhiteSpace(ex.Message) ? fallback : ex.Message);
    }
}
