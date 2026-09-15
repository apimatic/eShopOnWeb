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

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class OrderNotificationService
{
    public static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly CatalogContext _dbContext;
    private readonly IOrderMessagingProvider _messagingProvider;
    private readonly TimeProvider _timeProvider;

    public OrderNotificationService(
        CatalogContext dbContext,
        IOrderMessagingProvider messagingProvider,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _messagingProvider = messagingProvider;
        _timeProvider = timeProvider;
    }

    public async Task<ContactNumber> RegisterContactNumberAsync(
        string buyerId,
        string submittedNumber,
        CancellationToken cancellationToken)
    {
        PhoneNumberValidation validation;
        try
        {
            validation = await _messagingProvider.ValidatePhoneNumberAsync(submittedNumber, cancellationToken);
        }
        catch (MessagingProviderException)
        {
            throw new OrderFlowUnavailableException("Phone-number validation is temporarily unavailable.");
        }

        if (!validation.IsValid || string.IsNullOrWhiteSpace(validation.CanonicalPhoneNumber))
        {
            throw new OrderFlowValidationException("The phone number is not a usable destination.");
        }

        if (await _dbContext.ContactNumbers.AnyAsync(
                x => x.BuyerId == buyerId && x.PhoneNumber == validation.CanonicalPhoneNumber,
                cancellationToken))
        {
            throw new OrderFlowConflictException("That phone number is already registered.");
        }

        var contactNumber = new ContactNumber(buyerId, validation.CanonicalPhoneNumber, UtcNow());
        _dbContext.ContactNumbers.Add(contactNumber);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return contactNumber;
    }

    public Task<List<ContactNumber>> GetContactNumbersAsync(string buyerId, CancellationToken cancellationToken) =>
        _dbContext.ContactNumbers
            .AsNoTracking()
            .Where(x => x.BuyerId == buyerId)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);

    public async Task DeleteContactNumberAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken)
    {
        var contact = await _dbContext.ContactNumbers
            .SingleOrDefaultAsync(x => x.Id == contactNumberId && x.BuyerId == buyerId, cancellationToken)
            ?? throw new OrderFlowNotFoundException();

        var scheduled = await _dbContext.OrderNotifications
            .Where(x => x.ContactNumberId == contactNumberId && x.ScheduledFor != null && x.ProviderMessageSid != null)
            .ToListAsync(cancellationToken);

        await CancelScheduledMessagesAsync(scheduled, cancellationToken);
        _dbContext.ContactNumbers.Remove(contact);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyCollection<OrderLineInput> requestedItems,
        Address shippingAddress,
        CancellationToken cancellationToken)
    {
        var lines = ValidateAndCombineLines(requestedItems);
        var ids = lines.Select(x => x.CatalogItemId).ToArray();
        var catalogItems = await _dbContext.CatalogItems
            .AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .ToListAsync(cancellationToken);

        if (catalogItems.Count != ids.Length)
        {
            throw new OrderFlowValidationException("One or more catalog items do not exist.");
        }

        var itemById = catalogItems.ToDictionary(x => x.Id);
        var orderItems = lines.Select(line =>
        {
            var catalogItem = itemById[line.CatalogItemId];
            return new OrderItem(
                new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri),
                catalogItem.Price,
                line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shippingAddress, orderItems);
        _dbContext.Orders.Add(order);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await NotifyAllContactsAsync(
            order,
            NotificationKind.OrderPlaced,
            $"eShopOnWeb: Order #{order.Id} was placed.",
            null,
            cancellationToken);
        return order;
    }

    public async Task DispatchOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        order.Dispatch(UtcNow());
        await _dbContext.SaveChangesAsync(cancellationToken);

        await NotifyAllContactsAsync(
            order,
            NotificationKind.OrderDispatched,
            $"eShopOnWeb: Order #{order.Id} is on its way.",
            null,
            cancellationToken);

        await NotifyAllContactsAsync(
            order,
            NotificationKind.DeliveryFollowUp,
            $"eShopOnWeb: How did delivery of order #{order.Id} go?",
            UtcNow().Add(FollowUpDelay),
            cancellationToken);
    }

    public async Task CancelOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        if (order.Status == OrderStatus.Cancelled)
        {
            return;
        }

        var scheduledFollowUps = await _dbContext.OrderNotifications
            .Where(x => x.OrderId == orderId &&
                        x.Kind == NotificationKind.DeliveryFollowUp &&
                        x.ProviderMessageSid != null)
            .ToListAsync(cancellationToken);
        await CancelScheduledMessagesAsync(scheduledFollowUps, cancellationToken);

        order.Cancel(UtcNow());
        await _dbContext.SaveChangesAsync(cancellationToken);
        await NotifyAllContactsAsync(
            order,
            NotificationKind.OrderCancelled,
            $"eShopOnWeb: Order #{order.Id} was cancelled.",
            null,
            cancellationToken);
    }

    public async Task<List<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken)
    {
        var orders = await _dbContext.Orders
            .AsNoTracking()
            .Where(x => x.BuyerId == buyerId)
            .Include(x => x.OrderItems)
            .OrderByDescending(x => x.OrderDate)
            .ToListAsync(cancellationToken);
        await RefreshNotificationsAsync(
            _dbContext.OrderNotifications.Where(x => x.BuyerId == buyerId),
            cancellationToken);
        return orders;
    }

    public async Task<List<OrderNotification>> GetNotificationsForBuyerOrderAsync(
        string buyerId,
        int orderId,
        CancellationToken cancellationToken)
    {
        if (!await _dbContext.Orders.AnyAsync(x => x.Id == orderId && x.BuyerId == buyerId, cancellationToken))
        {
            throw new OrderFlowNotFoundException();
        }

        await RefreshNotificationsAsync(
            _dbContext.OrderNotifications.Where(x => x.OrderId == orderId && x.BuyerId == buyerId),
            cancellationToken);
        return await _dbContext.OrderNotifications
            .AsNoTracking()
            .Where(x => x.OrderId == orderId && x.BuyerId == buyerId)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
    }

    public Task<List<OrderNotification>> GetNotificationsForBuyerAsync(string buyerId, CancellationToken cancellationToken) =>
        _dbContext.OrderNotifications
            .AsNoTracking()
            .Where(x => x.BuyerId == buyerId)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);

    public async Task<OrderNotification> ResendAsync(
        int notificationId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 200)
        {
            throw new OrderFlowValidationException("An idempotency key of at most 200 characters is required.");
        }

        idempotencyKey = idempotencyKey.Trim();
        var existing = await _dbContext.OrderNotifications
            .SingleOrDefaultAsync(
                x => x.ResendOfNotificationId == notificationId && x.IdempotencyKey == idempotencyKey,
                cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var original = await _dbContext.OrderNotifications
            .SingleOrDefaultAsync(x => x.Id == notificationId, cancellationToken)
            ?? throw new OrderFlowNotFoundException();
        await TryRefreshAsync(original, cancellationToken);

        if (!CanResend(original.ProviderStatus))
        {
            throw new OrderFlowConflictException("Only a failed or undelivered message can be resent.");
        }

        if (string.IsNullOrWhiteSpace(original.Body) || original.ContentDisposedAt is not null)
        {
            throw new OrderFlowConflictException("Disposed message content cannot be resent.");
        }

        var contactStillExists = original.ContactNumberId.HasValue &&
            await _dbContext.ContactNumbers.AnyAsync(
                x => x.Id == original.ContactNumberId.Value && x.BuyerId == original.BuyerId,
                cancellationToken);
        if (!contactStillExists)
        {
            throw new OrderFlowConflictException("The destination is no longer registered.");
        }

        var resend = new OrderNotification(
            original.OrderId,
            original.BuyerId,
            original.ContactNumberId,
            original.Destination,
            NotificationKind.Resend,
            original.Body,
            UtcNow(),
            resendOfNotificationId: original.Id,
            idempotencyKey: idempotencyKey);
        _dbContext.OrderNotifications.Add(resend);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await SubmitNotificationAsync(resend, cancellationToken);
        return resend;
    }

    public async Task DisposeNotificationContentAsync(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _dbContext.OrderNotifications
            .SingleOrDefaultAsync(x => x.Id == notificationId, cancellationToken)
            ?? throw new OrderFlowNotFoundException();
        if (notification.ContentDisposedAt is not null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(notification.ProviderMessageSid))
        {
            ProviderMessageState state;
            try
            {
                state = await _messagingProvider.RedactContentAsync(notification.ProviderMessageSid, cancellationToken);
            }
            catch (MessagingProviderException)
            {
                throw new OrderFlowUnavailableException("Message content could not be disposed at the provider.");
            }

            notification.RecordProviderState(state, UtcNow());
        }

        notification.DisposeContent(UtcNow());
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<ReconciliationResult> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (from > to)
        {
            throw new OrderFlowValidationException("The from value must be earlier than or equal to to.");
        }

        IReadOnlyList<ProviderMessageRecord> providerRecords;
        try
        {
            providerRecords = await _messagingProvider.ListFromApplicationNumberAsync(cancellationToken);
        }
        catch (MessagingProviderException)
        {
            throw new OrderFlowUnavailableException("The provider reconciliation data is temporarily unavailable.");
        }

        var fromUtc = from.ToUniversalTime();
        var toUtc = to.ToUniversalTime();
        var providerInRange = providerRecords
            .Where(x => x.CreatedAt >= fromUtc && x.CreatedAt <= toUtc)
            .ToDictionary(x => x.Sid, StringComparer.Ordinal);
        var localInRange = await _dbContext.OrderNotifications
            .AsNoTracking()
            .Where(x => x.CreatedAt >= fromUtc && x.CreatedAt <= toUtc)
            .ToListAsync(cancellationToken);

        var entries = localInRange.Select(local =>
        {
            providerInRange.Remove(local.ProviderMessageSid ?? string.Empty, out var provider);
            return new ReconciliationEntry(
                local.Id,
                local.ProviderMessageSid,
                local.ProviderStatus,
                provider?.Status,
                provider is not null,
                true,
                local.CreatedAt,
                provider?.CreatedAt);
        }).ToList();

        entries.AddRange(providerInRange.Values.Select(provider =>
            new ReconciliationEntry(
                null,
                provider.Sid,
                null,
                provider.Status,
                true,
                false,
                null,
                provider.CreatedAt)));

        return new ReconciliationResult(fromUtc, toUtc, entries.OrderBy(x => x.ProviderCreatedAt ?? x.LocalCreatedAt).ToList());
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken) =>
        await _dbContext.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken)
        ?? throw new OrderFlowNotFoundException();

    private async Task NotifyAllContactsAsync(
        Order order,
        NotificationKind kind,
        string body,
        DateTimeOffset? scheduledFor,
        CancellationToken cancellationToken)
    {
        var contacts = await _dbContext.ContactNumbers
            .AsNoTracking()
            .Where(x => x.BuyerId == order.BuyerId)
            .ToListAsync(cancellationToken);

        foreach (var contact in contacts)
        {
            var notification = new OrderNotification(
                order.Id,
                order.BuyerId,
                contact.Id,
                contact.PhoneNumber,
                kind,
                body,
                UtcNow(),
                scheduledFor);
            _dbContext.OrderNotifications.Add(notification);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await SubmitNotificationAsync(notification, cancellationToken);
        }
    }

    private async Task SubmitNotificationAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            var state = notification.ScheduledFor.HasValue
                ? await _messagingProvider.ScheduleAsync(
                    notification.Destination,
                    notification.Body!,
                    notification.ScheduledFor.Value,
                    cancellationToken)
                : await _messagingProvider.SendAsync(
                    notification.Destination,
                    notification.Body!,
                    cancellationToken);
            notification.RecordProviderState(state, UtcNow());
        }
        catch (MessagingProviderException)
        {
            notification.RecordSubmissionFailure(UtcNow());
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task CancelScheduledMessagesAsync(
        IEnumerable<OrderNotification> notifications,
        CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            await TryRefreshAsync(notification, cancellationToken, throwWhenUnavailable: true);
            if (!string.Equals(notification.ProviderStatus, "scheduled", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(notification.ProviderStatus, "queued", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                var state = await _messagingProvider.CancelAsync(notification.ProviderMessageSid!, cancellationToken);
                notification.RecordProviderState(state, UtcNow());
            }
            catch (MessagingProviderException)
            {
                throw new OrderFlowUnavailableException("A scheduled follow-up could not be cancelled at the provider.");
            }
        }
    }

    private async Task RefreshNotificationsAsync(
        IQueryable<OrderNotification> query,
        CancellationToken cancellationToken)
    {
        var notifications = await query.Where(x => x.ProviderMessageSid != null).ToListAsync(cancellationToken);
        foreach (var notification in notifications)
        {
            await TryRefreshAsync(notification, cancellationToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task TryRefreshAsync(
        OrderNotification notification,
        CancellationToken cancellationToken,
        bool throwWhenUnavailable = false)
    {
        if (string.IsNullOrWhiteSpace(notification.ProviderMessageSid))
        {
            return;
        }

        try
        {
            var state = await _messagingProvider.GetAsync(notification.ProviderMessageSid, cancellationToken);
            notification.RecordProviderState(state, UtcNow());
        }
        catch (MessagingProviderException) when (!throwWhenUnavailable)
        {
            // A read failure leaves the last known provider state intact.
        }
        catch (MessagingProviderException)
        {
            throw new OrderFlowUnavailableException("A scheduled follow-up could not be checked at the provider.");
        }
    }

    private static List<OrderLineInput> ValidateAndCombineLines(IReadOnlyCollection<OrderLineInput> requestedItems)
    {
        if (requestedItems is null || requestedItems.Count == 0 || requestedItems.Any(x => x.CatalogItemId <= 0 || x.Quantity <= 0))
        {
            throw new OrderFlowValidationException("At least one catalog item with a positive quantity is required.");
        }

        return requestedItems
            .GroupBy(x => x.CatalogItemId)
            .Select(x => new OrderLineInput(x.Key, checked(x.Sum(y => y.Quantity))))
            .ToList();
    }

    private static bool CanResend(string status) =>
        string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "undelivered", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "submission_failed", StringComparison.OrdinalIgnoreCase);

    private DateTimeOffset UtcNow() => _timeProvider.GetUtcNow();
}

public sealed record OrderLineInput(int CatalogItemId, int Quantity);

public sealed record ReconciliationResult(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationEntry> Entries);

public sealed record ReconciliationEntry(
    int? NotificationId,
    string? ProviderMessageSid,
    string? LocalStatus,
    string? ProviderStatus,
    bool ExistsAtProvider,
    bool ExistsLocally,
    DateTimeOffset? LocalCreatedAt,
    DateTimeOffset? ProviderCreatedAt);

public class OrderFlowNotFoundException : Exception { }

public class OrderFlowValidationException : Exception
{
    public OrderFlowValidationException(string message) : base(message) { }
}

public class OrderFlowConflictException : Exception
{
    public OrderFlowConflictException(string message) : base(message) { }
}

public class OrderFlowUnavailableException : Exception
{
    public OrderFlowUnavailableException(string message) : base(message) { }
}
