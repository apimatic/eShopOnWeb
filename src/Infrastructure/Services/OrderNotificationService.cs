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

public sealed class OrderNotificationService
{
    public const int FollowUpDelayDays = 3;
    private static readonly SemaphoreSlim ResendGate = new(1, 1);
    private readonly CatalogContext _db;
    private readonly ITwilioMessagingClient _twilio;
    private readonly TimeProvider _clock;

    public OrderNotificationService(CatalogContext db, ITwilioMessagingClient twilio, TimeProvider clock)
    {
        _db = db;
        _twilio = twilio;
        _clock = clock;
    }

    public Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken) =>
        SendToActiveContactsAsync(order, NotificationKind.OrderPlaced,
            $"eShopOnWeb: order #{order.Id} was placed successfully.", null, cancellationToken);

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken)
    {
        await SendToActiveContactsAsync(order, NotificationKind.OrderDispatched,
            $"eShopOnWeb: order #{order.Id} has been dispatched and is on its way.", null, cancellationToken);
        await SendToActiveContactsAsync(order, NotificationKind.DeliveryFollowUp,
            $"eShopOnWeb: how did delivery of order #{order.Id} go?", 
            _clock.GetUtcNow().AddDays(FollowUpDelayDays), cancellationToken);
    }

    public Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken) =>
        SendToActiveContactsAsync(order, NotificationKind.OrderCancelled,
            $"eShopOnWeb: order #{order.Id} has been cancelled.", null, cancellationToken);

    public async Task CancelPendingFollowUpsForOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var pending = await _db.OrderNotifications
            .Where(x => x.OrderId == orderId && x.Kind == NotificationKind.DeliveryFollowUp &&
                        x.ProviderMessageSid != null && x.ProviderStatus != "canceled")
            .ToListAsync(cancellationToken);

        foreach (var notification in pending)
        {
            try
            {
                await CancelIfStillPendingAsync(notification, cancellationToken);
            }
            catch (TwilioProviderException exception)
            {
                notification.MarkCancellationFailure(exception.ProviderCode, _clock.GetUtcNow());
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeactivateContactAsync(ContactNumber contact, CancellationToken cancellationToken)
    {
        var scheduled = await _db.OrderNotifications
            .Where(x => x.ContactNumberId == contact.Id && x.Kind == NotificationKind.DeliveryFollowUp &&
                        x.ProviderMessageSid != null && x.ProviderStatus != "canceled")
            .ToListAsync(cancellationToken);

        foreach (var notification in scheduled)
        {
            await CancelIfStillPendingAsync(notification, cancellationToken);
        }

        contact.Deactivate(_clock.GetUtcNow());
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task RefreshProviderStatesAsync(IReadOnlyCollection<OrderNotification> notifications,
        CancellationToken cancellationToken)
    {
        foreach (var notification in notifications.Where(x => x.ProviderMessageSid is not null))
        {
            try
            {
                var provider = await _twilio.GetMessageAsync(notification.ProviderMessageSid!, cancellationToken);
                Apply(notification, provider);
            }
            catch (TwilioProviderException)
            {
                // The most recently persisted provider state remains reportable when polling is unavailable.
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<OrderNotification?> ResendAsync(int notificationId, string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await ResendGate.WaitAsync(cancellationToken);
        try
        {
            var priorAttempt = await _db.OrderNotifications
                .SingleOrDefaultAsync(x => x.ResendOfNotificationId == notificationId &&
                                           x.IdempotencyKey == idempotencyKey, cancellationToken);
            if (priorAttempt is not null)
            {
                return priorAttempt;
            }

            var original = await _db.OrderNotifications.SingleOrDefaultAsync(x => x.Id == notificationId,
                cancellationToken);
            if (original is null)
            {
                return null;
            }

            if (original.ProviderMessageSid is not null)
            {
                try
                {
                    Apply(original, await _twilio.GetMessageAsync(original.ProviderMessageSid, cancellationToken));
                    await _db.SaveChangesAsync(cancellationToken);
                }
                catch (TwilioProviderException)
                {
                    // A known terminal local status remains eligible if provider polling is unavailable.
                }
            }

            if (original.ContentRedacted || string.IsNullOrEmpty(original.Body) ||
                original.ProviderStatus is not ("failed" or "undelivered" or "local-failed"))
            {
                throw new InvalidOperationException("Only a failed or undelivered notification with retained content can be resent.");
            }

            var contact = await _db.ContactNumbers.SingleOrDefaultAsync(
                x => x.Id == original.ContactNumberId && x.BuyerId == original.BuyerId && x.IsActive,
                cancellationToken);
            if (contact is null)
            {
                throw new InvalidOperationException("The destination is no longer registered.");
            }

            var attempt = new OrderNotification(original.OrderId, original.BuyerId, contact.Id,
                NotificationKind.Resend, original.Body, _clock.GetUtcNow(), null, original.Id, idempotencyKey);
            _db.OrderNotifications.Add(attempt);
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                _db.Entry(attempt).State = EntityState.Detached;
                return await _db.OrderNotifications.SingleAsync(
                    x => x.ResendOfNotificationId == notificationId && x.IdempotencyKey == idempotencyKey,
                    cancellationToken);
            }

            await TrySendAsync(attempt, contact.CanonicalNumber, cancellationToken);
            return attempt;
        }
        finally
        {
            ResendGate.Release();
        }
    }

    public async Task RedactAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        if (notification.ContentRedacted)
        {
            return;
        }

        if (notification.ProviderMessageSid is not null)
        {
            await _twilio.RedactMessageContentAsync(notification.ProviderMessageSid, cancellationToken);
        }

        notification.Redact(_clock.GetUtcNow());
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task SendToActiveContactsAsync(Order order, NotificationKind kind, string body,
        DateTimeOffset? sendAt, CancellationToken cancellationToken)
    {
        var contacts = await _db.ContactNumbers
            .Where(x => x.BuyerId == order.BuyerId && x.IsActive)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);

        foreach (var contact in contacts)
        {
            var notification = new OrderNotification(order.Id, order.BuyerId, contact.Id, kind, body,
                _clock.GetUtcNow(), sendAt);
            _db.OrderNotifications.Add(notification);
            await _db.SaveChangesAsync(cancellationToken);
            await TrySendAsync(notification, contact.CanonicalNumber, cancellationToken);
        }
    }

    private async Task TrySendAsync(OrderNotification notification, string destination,
        CancellationToken cancellationToken)
    {
        try
        {
            var provider = await _twilio.SendAsync(destination, notification.Body!, notification.ScheduledFor,
                cancellationToken);
            Apply(notification, provider);
        }
        catch (TwilioProviderException exception)
        {
            notification.MarkProviderFailure(exception.ProviderCode, _clock.GetUtcNow());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            notification.MarkProviderFailure(null, _clock.GetUtcNow());
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            notification.MarkProviderFailure(null, _clock.GetUtcNow());
        }

        await _db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task CancelIfStillPendingAsync(OrderNotification notification,
        CancellationToken cancellationToken)
    {
        var provider = await _twilio.GetMessageAsync(notification.ProviderMessageSid!, cancellationToken);
        Apply(notification, provider);
        if (provider.Status is "scheduled" or "queued" or "accepted")
        {
            Apply(notification,
                await _twilio.CancelScheduledMessageAsync(notification.ProviderMessageSid!, cancellationToken));
        }
    }

    private void Apply(OrderNotification notification, ProviderMessage provider) =>
        notification.ApplyProviderState(provider.Sid, provider.Status, provider.ErrorCode, provider.DateSent,
            _clock.GetUtcNow());
}
