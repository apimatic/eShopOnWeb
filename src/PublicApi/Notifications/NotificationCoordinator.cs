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

namespace Microsoft.eShopWeb.PublicApi.Notifications;

public sealed class NotificationCoordinator
{
    private static readonly HashSet<string> DidNotReachStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "failed",
        "undelivered",
        "provider_rejected"
    };

    private readonly CatalogContext _context;
    private readonly ITwilioMessagingClient _twilio;
    private readonly TimeProvider _clock;

    public NotificationCoordinator(CatalogContext context, ITwilioMessagingClient twilio, TimeProvider clock)
    {
        _context = context;
        _twilio = twilio;
        _clock = clock;
    }

    public async Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken)
    {
        var content = $"Your eShopOnWeb order #{order.Id} has been placed.";
        await SendToCurrentContactsAsync(order, NotificationKind.OrderPlaced, content, null, cancellationToken);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken)
    {
        var numbers = await CurrentContactsAsync(order.BuyerId, cancellationToken);
        foreach (var number in numbers)
        {
            await CreateAndSendAsync(
                order.Id,
                number.Id,
                NotificationKind.OrderDispatched,
                $"Your eShopOnWeb order #{order.Id} is on its way.",
                null,
                null,
                null,
                cancellationToken);

            var sendAt = _clock.GetUtcNow().AddDays(3);
            await CreateAndSendAsync(
                order.Id,
                number.Id,
                NotificationKind.DeliveryFollowUp,
                $"How did delivery of your eShopOnWeb order #{order.Id} go?",
                sendAt,
                null,
                null,
                cancellationToken);
        }
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken)
    {
        await CancelScheduledFollowUpsAsync(order.Id, cancellationToken);
        await SendToCurrentContactsAsync(
            order,
            NotificationKind.OrderCancelled,
            $"Your eShopOnWeb order #{order.Id} has been cancelled.",
            null,
            cancellationToken);
    }

    public async Task<bool> CancelPendingMessagesForContactAsync(int contactNumberId, CancellationToken cancellationToken)
    {
        var scheduled = await _context.OrderNotifications
            .Where(x => x.ContactNumberId == contactNumberId &&
                        x.Kind == NotificationKind.DeliveryFollowUp &&
                        x.ProviderMessageSid != null &&
                        x.ProviderStatus != "canceled")
            .ToListAsync(cancellationToken);

        foreach (var notification in scheduled)
        {
            ProviderMessage current;
            try
            {
                current = await _twilio.FetchAsync(notification.ProviderMessageSid!, cancellationToken);
                ApplyProviderState(notification, current);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            if (current.Status.Equals("scheduled", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var cancelled = await CancelWithRetriesAsync(notification.ProviderMessageSid!, cancellationToken);
                    ApplyProviderState(notification, cancelled);
                }
                catch (Exception) when (!cancellationToken.IsCancellationRequested)
                {
                    return false;
                }
            }
            else if (current.Status is "accepted" or "queued" or "sending")
            {
                return false;
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task RefreshAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        var changed = false;
        foreach (var notification in notifications.Where(x => x.ProviderMessageSid != null))
        {
            try
            {
                var providerMessage = await _twilio.FetchAsync(notification.ProviderMessageSid!, cancellationToken);
                ApplyProviderState(notification, providerMessage);
                changed = true;
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // Reads remain available with the last known provider state during an outage.
            }
        }

        if (changed)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<ResendResult> ResendAsync(
        int notificationId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var existing = await _context.OrderNotifications.SingleOrDefaultAsync(
            x => x.ResendOfNotificationId == notificationId && x.IdempotencyKey == idempotencyKey,
            cancellationToken);
        if (existing != null)
        {
            return new ResendResult(existing.Id, ResendOutcome.Existing);
        }

        var source = await _context.OrderNotifications
            .Include(x => x.Order)
            .Include(x => x.ContactNumber)
            .SingleOrDefaultAsync(x => x.Id == notificationId, cancellationToken);
        if (source == null)
        {
            return new ResendResult(null, ResendOutcome.NotFound);
        }

        if (source.ProviderMessageSid != null)
        {
            try
            {
                var current = await _twilio.FetchAsync(source.ProviderMessageSid, cancellationToken);
                ApplyProviderState(source, current);
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                return new ResendResult(null, ResendOutcome.ProviderUnavailable);
            }
        }

        if (!DidNotReachStatuses.Contains(source.ProviderStatus) ||
            string.IsNullOrWhiteSpace(source.Content) ||
            source.ContactNumber == null)
        {
            return new ResendResult(null, ResendOutcome.NotEligible);
        }

        if (source.Order?.Status == OrderState.Cancelled && source.Kind == NotificationKind.DeliveryFollowUp)
        {
            return new ResendResult(null, ResendOutcome.NotEligible);
        }

        try
        {
            var resent = await CreateAndSendAsync(
                source.OrderId,
                source.ContactNumber.Id,
                NotificationKind.Resend,
                source.Content,
                null,
                source.Id,
                idempotencyKey,
                cancellationToken);
            return new ResendResult(resent.Id, ResendOutcome.Created);
        }
        catch (DbUpdateException)
        {
            // The database uniqueness constraint is the cross-process idempotency guard.
            // If another request won the race, return its durable notification identifier.
            _context.ChangeTracker.Clear();
            existing = await _context.OrderNotifications.SingleOrDefaultAsync(
                x => x.ResendOfNotificationId == notificationId && x.IdempotencyKey == idempotencyKey,
                cancellationToken);
            if (existing != null)
            {
                return new ResendResult(existing.Id, ResendOutcome.Existing);
            }

            throw;
        }
    }

    public async Task<ContentDisposalOutcome> DisposeContentAsync(
        int notificationId,
        CancellationToken cancellationToken)
    {
        var notification = await _context.OrderNotifications
            .SingleOrDefaultAsync(x => x.Id == notificationId, cancellationToken);
        if (notification == null)
        {
            return ContentDisposalOutcome.NotFound;
        }

        if (notification.ContentDisposedAt.HasValue)
        {
            return ContentDisposalOutcome.Disposed;
        }

        if (notification.ProviderMessageSid != null)
        {
            try
            {
                var providerMessage = await _twilio.RedactAsync(notification.ProviderMessageSid, cancellationToken);
                ApplyProviderState(notification, providerMessage);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                return ContentDisposalOutcome.ProviderUnavailable;
            }
        }

        notification.DisposeContent(_clock.GetUtcNow());
        await _context.SaveChangesAsync(cancellationToken);
        return ContentDisposalOutcome.Disposed;
    }

    public async Task<IReadOnlyList<ReconciliationEntry>> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var providerMessages = await _twilio.ListAsync(from, to, cancellationToken);
        var providerSids = new HashSet<string>(providerMessages.Select(x => x.Sid), StringComparer.Ordinal);
        var localMessages = await _context.OrderNotifications
            .Where(x => x.ProviderMessageSid != null &&
                        ((x.CreatedAt >= from && x.CreatedAt <= to) || providerSids.Contains(x.ProviderMessageSid)))
            .ToListAsync(cancellationToken);
        var localBySid = localMessages.ToDictionary(x => x.ProviderMessageSid!, StringComparer.Ordinal);
        var result = new List<ReconciliationEntry>();

        foreach (var provider in providerMessages)
        {
            if (localBySid.TryGetValue(provider.Sid, out var local))
            {
                ApplyProviderState(local, provider);
                result.Add(new ReconciliationEntry(
                    "matched",
                    provider.Sid,
                    local.Id,
                    provider.Status,
                    local.ProviderStatus,
                    provider.DateSent ?? provider.DateCreated));
            }
            else
            {
                result.Add(new ReconciliationEntry(
                    "provider_only",
                    provider.Sid,
                    null,
                    provider.Status,
                    null,
                    provider.DateSent ?? provider.DateCreated));
            }
        }

        result.AddRange(localMessages
            .Where(x => x.CreatedAt >= from && x.CreatedAt <= to && !providerSids.Contains(x.ProviderMessageSid!))
            .Select(x => new ReconciliationEntry(
                "eshop_only",
                x.ProviderMessageSid,
                x.Id,
                null,
                x.ProviderStatus,
                x.ProviderDateSent ?? x.ProviderDateCreated ?? x.CreatedAt)));

        await _context.SaveChangesAsync(cancellationToken);
        return result.OrderBy(x => x.OccurredAt).ThenBy(x => x.ProviderMessageSid).ToList();
    }

    private async Task SendToCurrentContactsAsync(
        Order order,
        NotificationKind kind,
        string content,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        var numbers = await CurrentContactsAsync(order.BuyerId, cancellationToken);
        foreach (var number in numbers)
        {
            await CreateAndSendAsync(
                order.Id,
                number.Id,
                kind,
                content,
                sendAt,
                null,
                null,
                cancellationToken);
        }
    }

    private Task<List<ContactNumber>> CurrentContactsAsync(string ownerId, CancellationToken cancellationToken) =>
        _context.ContactNumbers.Where(x => x.OwnerId == ownerId).OrderBy(x => x.Id).ToListAsync(cancellationToken);

    private async Task<OrderNotification> CreateAndSendAsync(
        int orderId,
        int contactNumberId,
        NotificationKind kind,
        string content,
        DateTimeOffset? sendAt,
        int? resendOfNotificationId,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow();
        var notification = new OrderNotification(
            orderId,
            contactNumberId,
            kind,
            content,
            now,
            sendAt,
            resendOfNotificationId,
            idempotencyKey);
        _context.OrderNotifications.Add(notification);
        await _context.SaveChangesAsync(cancellationToken);

        var number = await _context.ContactNumbers
            .Where(x => x.Id == contactNumberId)
            .Select(x => x.CanonicalNumber)
            .SingleAsync(cancellationToken);

        try
        {
            // Sends are intentionally attempted once. Twilio's create-message API has no
            // idempotency key, so retrying an ambiguous response can duplicate a paid SMS.
            var providerMessage = await _twilio.SendAsync(number, content, sendAt, cancellationToken);
            notification.RecordProviderState(
                providerMessage.Sid,
                providerMessage.Status,
                providerMessage.ErrorCode,
                providerMessage.DateCreated,
                providerMessage.DateSent,
                _clock.GetUtcNow());
        }
        catch (TwilioProviderException exception)
        {
            notification.RecordProviderFailure(exception.ProviderErrorCode, _clock.GetUtcNow());
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            notification.RecordProviderFailure(null, _clock.GetUtcNow());
        }

        await _context.SaveChangesAsync(cancellationToken);
        return notification;
    }

    private async Task CancelScheduledFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var scheduled = await _context.OrderNotifications
            .Where(x => x.OrderId == orderId &&
                        x.Kind == NotificationKind.DeliveryFollowUp &&
                        x.ProviderMessageSid != null &&
                        x.ProviderStatus != "canceled")
            .ToListAsync(cancellationToken);

        foreach (var notification in scheduled)
        {
            try
            {
                var providerMessage = await CancelWithRetriesAsync(notification.ProviderMessageSid!, cancellationToken);
                ApplyProviderState(notification, providerMessage);
            }
            catch (TwilioProviderException exception)
            {
                notification.RecordProviderFailure(exception.ProviderErrorCode, _clock.GetUtcNow());
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                notification.RecordProviderFailure(null, _clock.GetUtcNow());
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task<ProviderMessage> CancelWithRetriesAsync(string messageSid, CancellationToken cancellationToken)
    {
        Exception? lastException = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                return await _twilio.CancelAsync(messageSid, cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                lastException = exception;
                if (attempt < 2)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(200 * (attempt + 1)), cancellationToken);
                }
            }
        }

        throw lastException!;
    }

    private void ApplyProviderState(OrderNotification notification, ProviderMessage providerMessage)
    {
        notification.RefreshProviderState(
            providerMessage.Status,
            providerMessage.ErrorCode,
            providerMessage.DateCreated,
            providerMessage.DateSent,
            _clock.GetUtcNow());
    }
}

public enum ResendOutcome
{
    Created,
    Existing,
    NotFound,
    NotEligible,
    ProviderUnavailable
}

public record ResendResult(int? NotificationId, ResendOutcome Outcome);

public enum ContentDisposalOutcome
{
    Disposed,
    NotFound,
    ProviderUnavailable
}

public record ReconciliationEntry(
    string Match,
    string? ProviderMessageSid,
    int? NotificationId,
    string? ProviderStatus,
    string? EshopStatus,
    DateTimeOffset? OccurredAt);
