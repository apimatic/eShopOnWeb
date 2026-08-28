using System;
using System.Collections.Concurrent;
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

public sealed class OrderNotificationService
{
    public static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ResendLocks = new(StringComparer.Ordinal);
    private readonly CatalogContext _db;
    private readonly ISmsProvider _provider;
    private readonly TimeProvider _timeProvider;

    public OrderNotificationService(CatalogContext db, ISmsProvider provider, TimeProvider timeProvider)
    {
        _db = db;
        _provider = provider;
        _timeProvider = timeProvider;
    }

    public Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken)
    {
        return SendToCurrentContactsAsync(
            order,
            NotificationKind.OrderPlaced,
            $"eShopOnWeb: Order #{order.Id} was placed.",
            null,
            cancellationToken);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken)
    {
        var contacts = await GetContactsAsync(order.BuyerId, cancellationToken);
        foreach (var contact in contacts)
        {
            await CreateAndSendAsync(
                order,
                contact,
                NotificationKind.OrderDispatched,
                $"eShopOnWeb: Order #{order.Id} is on its way.",
                null,
                cancellationToken);

            await CreateAndSendAsync(
                order,
                contact,
                NotificationKind.DeliveryFollowUp,
                $"eShopOnWeb: How did delivery of order #{order.Id} go?",
                _timeProvider.GetUtcNow().Add(DeliveryFollowUpDelay),
                cancellationToken);
        }
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken)
    {
        await RequestFollowUpCancellationAsync(order.Id, null, cancellationToken);
        await SendToCurrentContactsAsync(
            order,
            NotificationKind.OrderCancelled,
            $"eShopOnWeb: Order #{order.Id} was cancelled.",
            null,
            cancellationToken);
    }

    public async Task<bool> CancelScheduledMessagesForContactAsync(int contactNumberId, CancellationToken cancellationToken)
    {
        return await RequestFollowUpCancellationAsync(null, contactNumberId, cancellationToken);
    }

    public async Task RetryRequestedCancellationsAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var notifications = await _db.OrderNotifications
            .Where(x => x.Kind == NotificationKind.DeliveryFollowUp &&
                        x.CancellationRequested &&
                        x.ProviderMessageSid != null &&
                        x.ProviderStatus != "canceled" &&
                        x.ScheduledFor > now)
            .ToListAsync(cancellationToken);

        foreach (var notification in notifications)
        {
            await TryCancelAtProviderAsync(notification, cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task RefreshAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications.Where(x => x.ProviderMessageSid is not null && !IsTerminal(x.ProviderStatus)))
        {
            try
            {
                notification.ApplyProviderState(
                    await _provider.GetMessageAsync(notification.ProviderMessageSid!, cancellationToken));
            }
            catch (SmsProviderException)
            {
                // Preserve the last provider state. Read paths remain available during provider outages.
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<ResendResult> ResendAsync(
        int originalNotificationId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var resendLock = ResendLocks.GetOrAdd(idempotencyKey, _ => new SemaphoreSlim(1, 1));
        await resendLock.WaitAsync(cancellationToken);
        try
        {
            var previous = await _db.OrderNotifications
                .SingleOrDefaultAsync(x => x.ResendIdempotencyKey == idempotencyKey, cancellationToken);
            if (previous is not null)
            {
                return previous.OriginalNotificationId == originalNotificationId
                    ? ResendResult.Created(previous.Id)
                    : ResendResult.Failed(ResendFailure.IdempotencyKeyConflict);
            }

            var original = await _db.OrderNotifications
                .SingleOrDefaultAsync(x => x.Id == originalNotificationId, cancellationToken);
            if (original is null)
            {
                return ResendResult.Failed(ResendFailure.NotFound);
            }

            if (original.Kind == NotificationKind.DeliveryFollowUp ||
                !IsFailedDelivery(original.ProviderStatus))
            {
                return ResendResult.Failed(ResendFailure.NotEligible);
            }

            if (string.IsNullOrWhiteSpace(original.Body))
            {
                return ResendResult.Failed(ResendFailure.ContentDisposed);
            }

            var contact = original.ContactNumberId is null
                ? null
                : await _db.ContactNumbers.SingleOrDefaultAsync(
                    x => x.Id == original.ContactNumberId && x.BuyerId == original.BuyerId,
                    cancellationToken);
            if (contact is null)
            {
                return ResendResult.Failed(ResendFailure.ContactRemoved);
            }

            var resend = new OrderNotification(
                original.OrderId,
                original.BuyerId,
                contact.Id,
                NotificationKind.Resend,
                original.Body,
                originalNotificationId: original.Id,
                resendIdempotencyKey: idempotencyKey);

            _db.OrderNotifications.Add(resend);
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Another application instance may have claimed this key after our read.
                // The unique index prevents both instances from reaching the provider.
                _db.Entry(resend).State = EntityState.Detached;
                var winner = await _db.OrderNotifications
                    .AsNoTracking()
                    .SingleOrDefaultAsync(x => x.ResendIdempotencyKey == idempotencyKey, cancellationToken);
                if (winner is not null && winner.OriginalNotificationId == originalNotificationId)
                {
                    return ResendResult.Created(winner.Id);
                }

                throw;
            }

            await SendExistingAsync(resend, contact.E164Number, cancellationToken);
            return ResendResult.Created(resend.Id);
        }
        finally
        {
            resendLock.Release();
            ResendLocks.TryRemove(new KeyValuePair<string, SemaphoreSlim>(idempotencyKey, resendLock));
        }
    }

    public async Task<ContentDisposalResult> DisposeContentAsync(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _db.OrderNotifications
            .SingleOrDefaultAsync(x => x.Id == notificationId, cancellationToken);
        if (notification is null)
        {
            return ContentDisposalResult.NotFound;
        }

        if (notification.ContentDisposedAt.HasValue)
        {
            return ContentDisposalResult.Disposed;
        }

        if (notification.ProviderMessageSid is not null)
        {
            try
            {
                notification.ApplyProviderState(
                    await _provider.RedactMessageAsync(notification.ProviderMessageSid, cancellationToken));
            }
            catch (SmsProviderException)
            {
                return ContentDisposalResult.ProviderUnavailable;
            }
        }

        notification.DisposeContent(_timeProvider.GetUtcNow());
        await _db.SaveChangesAsync(cancellationToken);
        return ContentDisposalResult.Disposed;
    }

    public async Task<ReconciliationData> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var providerMessages = await _provider.ListMessagesAsync(from, to, cancellationToken);
        var providerSids = providerMessages.Select(x => x.Sid).Distinct(StringComparer.Ordinal).ToArray();

        var local = await _db.OrderNotifications
            .Where(x => (x.CreatedAt >= from && x.CreatedAt <= to) ||
                        (x.ProviderCreatedAt >= from && x.ProviderCreatedAt <= to) ||
                        (x.ProviderSentAt >= from && x.ProviderSentAt <= to))
            .ToListAsync(cancellationToken);

        var knownSids = local
            .Where(x => x.ProviderMessageSid is not null)
            .Select(x => x.ProviderMessageSid!)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var sidChunk in providerSids.Where(x => !knownSids.Contains(x)).Chunk(500))
        {
            local.AddRange(await _db.OrderNotifications
                .Where(x => x.ProviderMessageSid != null && sidChunk.Contains(x.ProviderMessageSid))
                .ToListAsync(cancellationToken));
        }

        var bySid = local
            .Where(x => x.ProviderMessageSid is not null)
            .ToDictionary(x => x.ProviderMessageSid!, StringComparer.Ordinal);

        foreach (var message in providerMessages)
        {
            if (bySid.TryGetValue(message.Sid, out var notification))
            {
                notification.ApplyProviderState(message);
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        return new ReconciliationData(providerMessages, local);
    }

    private async Task SendToCurrentContactsAsync(
        Order order,
        NotificationKind kind,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        var contacts = await GetContactsAsync(order.BuyerId, cancellationToken);
        foreach (var contact in contacts)
        {
            await CreateAndSendAsync(order, contact, kind, body, sendAt, cancellationToken);
        }
    }

    private Task<List<ContactNumber>> GetContactsAsync(string buyerId, CancellationToken cancellationToken)
    {
        return _db.ContactNumbers
            .AsNoTracking()
            .Where(x => x.BuyerId == buyerId)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
    }

    private async Task CreateAndSendAsync(
        Order order,
        ContactNumber contact,
        NotificationKind kind,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        var notification = new OrderNotification(order.Id, order.BuyerId, contact.Id, kind, body, sendAt);
        _db.OrderNotifications.Add(notification);
        await _db.SaveChangesAsync(cancellationToken);
        await SendExistingAsync(notification, contact.E164Number, cancellationToken);
    }

    private async Task SendExistingAsync(
        OrderNotification notification,
        string e164Destination,
        CancellationToken cancellationToken)
    {
        try
        {
            notification.ApplyProviderState(
                await _provider.SendMessageAsync(
                    e164Destination,
                    notification.Body!,
                    notification.ScheduledFor,
                    cancellationToken));
        }
        catch (SmsProviderException ex)
        {
            notification.MarkProviderRejected(ex.ProviderErrorCode);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            notification.MarkProviderRejected(null);
        }

        await _db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<bool> RequestFollowUpCancellationAsync(
        int? orderId,
        int? contactNumberId,
        CancellationToken cancellationToken)
    {
        var query = _db.OrderNotifications.Where(x => x.Kind == NotificationKind.DeliveryFollowUp);
        if (orderId.HasValue)
        {
            query = query.Where(x => x.OrderId == orderId.Value);
        }

        if (contactNumberId.HasValue)
        {
            query = query.Where(x => x.ContactNumberId == contactNumberId.Value);
        }

        var notifications = await query.ToListAsync(cancellationToken);
        foreach (var notification in notifications)
        {
            notification.RequestCancellation();
        }

        await _db.SaveChangesAsync(cancellationToken);

        var allSafe = true;
        foreach (var notification in notifications)
        {
            allSafe &= await TryCancelAtProviderAsync(notification, cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return allSafe;
    }

    private async Task<bool> TryCancelAtProviderAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        if (notification.ProviderMessageSid is null || IsTerminal(notification.ProviderStatus))
        {
            return true;
        }

        try
        {
            var current = await _provider.GetMessageAsync(notification.ProviderMessageSid, cancellationToken);
            notification.ApplyProviderState(current);
            if (current.Status == "scheduled")
            {
                notification.ApplyProviderState(
                    await _provider.CancelMessageAsync(notification.ProviderMessageSid, cancellationToken));
            }

            return notification.ProviderStatus != "scheduled";
        }
        catch (SmsProviderException)
        {
            return false;
        }
    }

    private static bool IsTerminal(string status)
    {
        return status is "delivered" or "undelivered" or "failed" or "canceled" or "read";
    }

    private static bool IsFailedDelivery(string status)
    {
        return status is "undelivered" or "failed" or NotificationStatuses.ProviderRejected;
    }
}

public sealed record ResendResult(int? NotificationId, ResendFailure? Failure)
{
    public static ResendResult Created(int notificationId) => new(notificationId, null);
    public static ResendResult Failed(ResendFailure failure) => new(null, failure);
}

public enum ResendFailure
{
    NotFound,
    NotEligible,
    ContentDisposed,
    ContactRemoved,
    IdempotencyKeyConflict
}

public enum ContentDisposalResult
{
    Disposed,
    NotFound,
    ProviderUnavailable
}

public sealed record ReconciliationData(
    IReadOnlyList<SmsMessageSnapshot> ProviderMessages,
    IReadOnlyList<OrderNotification> LocalNotifications);
