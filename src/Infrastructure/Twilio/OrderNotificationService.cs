using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public sealed class OrderNotificationService : IOrderNotificationService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ResendLocks = new(StringComparer.Ordinal);
    private readonly CatalogContext _context;
    private readonly ISmsProvider _provider;

    public OrderNotificationService(CatalogContext context, ISmsProvider provider)
    {
        _context = context;
        _provider = provider;
    }

    public async Task<ContactRegistrationResult> RegisterContactAsync(
        string buyerId,
        string input,
        CancellationToken cancellationToken)
    {
        PhoneValidationResult validation;
        try
        {
            validation = await _provider.ValidatePhoneNumberAsync(input, cancellationToken);
        }
        catch (SmsProviderException exception) when (IsCallerValidationError(exception.StatusCode))
        {
            return new ContactRegistrationResult(ContactRegistrationOutcome.Invalid, null, "The phone number is not a usable destination.");
        }
        catch (SmsProviderException)
        {
            return new ContactRegistrationResult(ContactRegistrationOutcome.ProviderUnavailable, null, "Phone number validation is temporarily unavailable.");
        }

        if (!validation.IsValid || string.IsNullOrWhiteSpace(validation.CanonicalNumber))
        {
            return new ContactRegistrationResult(ContactRegistrationOutcome.Invalid, null, "The phone number is not a usable destination.");
        }

        var existing = await _context.ContactNumbers.SingleOrDefaultAsync(
            x => x.BuyerId == buyerId && x.CanonicalNumber == validation.CanonicalNumber && x.DeletedAt == null,
            cancellationToken);
        if (existing is not null)
        {
            return new ContactRegistrationResult(ContactRegistrationOutcome.Duplicate, existing, "That phone number is already registered.");
        }

        var contact = new ContactNumber(buyerId, validation.CanonicalNumber, DateTimeOffset.UtcNow);
        _context.ContactNumbers.Add(contact);
        await _context.SaveChangesAsync(cancellationToken);
        return new ContactRegistrationResult(ContactRegistrationOutcome.Created, contact, null);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListContactsAsync(string buyerId, CancellationToken cancellationToken) =>
        await _context.ContactNumbers
            .AsNoTracking()
            .Where(x => x.BuyerId == buyerId && x.DeletedAt == null)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);

    public async Task<ContactDeletionResult> DeleteContactAsync(
        string buyerId,
        int contactNumberId,
        CancellationToken cancellationToken)
    {
        var contact = await _context.ContactNumbers.SingleOrDefaultAsync(
            x => x.Id == contactNumberId && x.BuyerId == buyerId && x.DeletedAt == null,
            cancellationToken);
        if (contact is null)
        {
            return new ContactDeletionResult(ContactDeletionOutcome.NotFound);
        }

        var scheduled = await _context.OrderNotifications
            .Where(x => x.ContactNumberId == contact.Id &&
                        x.Kind == NotificationKind.DeliveryFollowUp &&
                        x.ProviderMessageId != null &&
                        (x.ProviderStatus == "scheduled" || x.ProviderStatus == "queued" || x.ProviderStatus == "accepted"))
            .ToListAsync(cancellationToken);

        foreach (var notification in scheduled)
        {
            try
            {
                var snapshot = await _provider.CancelAsync(notification.ProviderMessageId!, cancellationToken);
                notification.ApplyProviderSnapshot(snapshot, DateTimeOffset.UtcNow);
                if (!string.Equals(notification.ProviderStatus, "canceled", StringComparison.OrdinalIgnoreCase))
                {
                    return new ContactDeletionResult(ContactDeletionOutcome.ProviderUnavailable);
                }
            }
            catch (SmsProviderException)
            {
                notification.MarkProviderFailure("Provider cancellation could not be confirmed.", DateTimeOffset.UtcNow);
                await _context.SaveChangesAsync(cancellationToken);
                return new ContactDeletionResult(ContactDeletionOutcome.ProviderUnavailable);
            }
        }

        contact.Delete(DateTimeOffset.UtcNow);
        await _context.SaveChangesAsync(cancellationToken);
        return new ContactDeletionResult(ContactDeletionOutcome.Deleted);
    }

    public Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken) =>
        SendToActiveContactsSafelyAsync(
            order,
            NotificationKind.OrderPlaced,
            $"Your eShopOnWeb order #{order.Id} has been placed.",
            null,
            cancellationToken);

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken)
    {
        try
        {
            await SendToActiveContactsAsync(
                order,
                NotificationKind.OrderDispatched,
                $"Your eShopOnWeb order #{order.Id} is on its way.",
                null,
                cancellationToken);

            var followUpAt = (order.DispatchedAt ?? DateTimeOffset.UtcNow).AddDays(3);
            await SendToActiveContactsAsync(
                order,
                NotificationKind.DeliveryFollowUp,
                $"How did delivery of eShopOnWeb order #{order.Id} go?",
                followUpAt,
                cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Notification persistence/provider failures never reverse dispatch.
        }
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken)
    {
        try
        {
            var pending = await _context.OrderNotifications
                .Where(x => x.OrderId == order.Id &&
                            x.Kind == NotificationKind.DeliveryFollowUp &&
                            x.ProviderMessageId != null &&
                            x.ProviderStatus != "canceled" &&
                            x.ProviderSentAt == null)
                .ToListAsync(cancellationToken);

            foreach (var notification in pending)
            {
                notification.RequestCancellation();
            }

            await _context.SaveChangesAsync(cancellationToken);
            foreach (var notification in pending)
            {
                await TryCancelAsync(notification, cancellationToken);
            }

            await _context.SaveChangesAsync(cancellationToken);
            await SendToActiveContactsAsync(
                order,
                NotificationKind.OrderCancelled,
                $"Your eShopOnWeb order #{order.Id} has been cancelled.",
                null,
                cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // The durable CancellationRequested flag is retried by the hosted worker.
        }
    }

    public async Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(
        int orderId,
        string buyerId,
        CancellationToken cancellationToken)
    {
        var notifications = await _context.OrderNotifications
            .Where(x => x.OrderId == orderId && x.BuyerId == buyerId)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        foreach (var notification in notifications.Where(x => x.ProviderMessageId is not null))
        {
            try
            {
                var snapshot = await _provider.FetchAsync(notification.ProviderMessageId!, cancellationToken);
                notification.ApplyProviderSnapshot(snapshot, DateTimeOffset.UtcNow);
            }
            catch (SmsProviderException)
            {
                notification.MarkProviderFailure("Provider status refresh failed; the previous outcome is shown.", DateTimeOffset.UtcNow);
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
        return notifications;
    }

    public async Task<ResendNotificationResult> ResendAsync(
        int notificationId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var lockKey = $"{notificationId}:{idempotencyKey}";
        var gate = ResendLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var existing = await _context.OrderNotifications.SingleOrDefaultAsync(
                x => x.OriginalNotificationId == notificationId && x.ResendIdempotencyKey == idempotencyKey,
                cancellationToken);
            if (existing is not null)
            {
                return new ResendNotificationResult(ResendNotificationOutcome.Existing, existing);
            }

            var original = await _context.OrderNotifications.SingleOrDefaultAsync(x => x.Id == notificationId, cancellationToken);
            if (original is null)
            {
                return new ResendNotificationResult(ResendNotificationOutcome.NotFound, null);
            }

            if (original.ProviderMessageId is not null)
            {
                try
                {
                    var snapshot = await _provider.FetchAsync(original.ProviderMessageId, cancellationToken);
                    original.ApplyProviderSnapshot(snapshot, DateTimeOffset.UtcNow);
                    await _context.SaveChangesAsync(cancellationToken);
                }
                catch (SmsProviderException)
                {
                    original.MarkProviderFailure("Provider status refresh failed; resend eligibility uses the previous outcome.", DateTimeOffset.UtcNow);
                }
            }

            if (original.Content is null)
            {
                return new ResendNotificationResult(ResendNotificationOutcome.ContentDisposed, null);
            }

            if (original.Kind == NotificationKind.DeliveryFollowUp || !DidNotReachShopper(original.ProviderStatus))
            {
                return new ResendNotificationResult(ResendNotificationOutcome.NotEligible, null);
            }

            var contactIsActive = await _context.ContactNumbers.AnyAsync(
                x => x.Id == original.ContactNumberId && x.BuyerId == original.BuyerId && x.DeletedAt == null,
                cancellationToken);
            if (!contactIsActive)
            {
                return new ResendNotificationResult(ResendNotificationOutcome.ContactRemoved, null);
            }

            var resend = new OrderNotification(
                original.OrderId,
                original.BuyerId,
                original.ContactNumberId,
                original.Destination,
                NotificationKind.Resend,
                original.Content,
                DateTimeOffset.UtcNow,
                originalNotificationId: original.Id,
                resendIdempotencyKey: idempotencyKey);
            _context.OrderNotifications.Add(resend);
            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                _context.Entry(resend).State = EntityState.Detached;
                var winner = await _context.OrderNotifications.SingleOrDefaultAsync(
                    x => x.OriginalNotificationId == notificationId && x.ResendIdempotencyKey == idempotencyKey,
                    cancellationToken);
                if (winner is not null)
                {
                    return new ResendNotificationResult(ResendNotificationOutcome.Existing, winner);
                }

                throw;
            }

            await SendPersistedNotificationAsync(resend, cancellationToken);
            return new ResendNotificationResult(ResendNotificationOutcome.Created, resend);
        }
        finally
        {
            gate.Release();
            ResendLocks.TryRemove(lockKey, out _);
        }
    }

    public async Task<ContentDisposalResult> DisposeContentAsync(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _context.OrderNotifications.SingleOrDefaultAsync(x => x.Id == notificationId, cancellationToken);
        if (notification is null)
        {
            return new ContentDisposalResult(ContentDisposalOutcome.NotFound);
        }

        if (notification.Content is null)
        {
            return new ContentDisposalResult(ContentDisposalOutcome.AlreadyDisposed);
        }

        if (notification.ProviderMessageId is not null)
        {
            try
            {
                var snapshot = await _provider.DisposeContentAsync(notification.ProviderMessageId, cancellationToken);
                notification.ApplyProviderSnapshot(snapshot, DateTimeOffset.UtcNow);
            }
            catch (SmsProviderException)
            {
                return new ContentDisposalResult(ContentDisposalOutcome.ProviderUnavailable);
            }
        }

        notification.MarkContentDisposed(DateTimeOffset.UtcNow);
        await _context.SaveChangesAsync(cancellationToken);
        return new ContentDisposalResult(ContentDisposalOutcome.Disposed);
    }

    public async Task<NotificationReconciliationResult> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var providerMessages = await _provider.ListAsync(from, to, cancellationToken);
        var localMessages = await _context.OrderNotifications
            .AsNoTracking()
            .Where(x => x.CreatedAt > from && x.CreatedAt < to)
            .ToListAsync(cancellationToken);
        var localBySid = localMessages
            .Where(x => x.ProviderMessageId is not null)
            .ToDictionary(x => x.ProviderMessageId!, StringComparer.Ordinal);
        var matchedLocalIds = new HashSet<int>();
        var entries = new List<NotificationReconciliationEntry>();

        foreach (var provider in providerMessages)
        {
            localBySid.TryGetValue(provider.ProviderMessageId ?? string.Empty, out var local);
            if (local is not null)
            {
                matchedLocalIds.Add(local.Id);
            }

            entries.Add(new NotificationReconciliationEntry(
                local is null ? "provider-only" : "matched",
                provider.ProviderMessageId,
                local?.Id,
                local?.OrderId,
                provider.Status,
                provider.SentAt));
        }

        entries.AddRange(localMessages
            .Where(x => !matchedLocalIds.Contains(x.Id))
            .Select(x => new NotificationReconciliationEntry(
                "local-only",
                x.ProviderMessageId,
                x.Id,
                x.OrderId,
                x.ProviderStatus,
                x.ProviderSentAt)));

        return new NotificationReconciliationResult(from, to, entries);
    }

    public async Task RetryPendingCancellationsAsync(CancellationToken cancellationToken)
    {
        var pending = await _context.OrderNotifications
            .Where(x => x.CancellationRequested && x.ProviderMessageId != null)
            .Take(100)
            .ToListAsync(cancellationToken);
        foreach (var notification in pending)
        {
            await TryCancelAsync(notification, cancellationToken);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task SendToActiveContactsSafelyAsync(
        Order order,
        NotificationKind kind,
        string content,
        DateTimeOffset? scheduledFor,
        CancellationToken cancellationToken)
    {
        try
        {
            await SendToActiveContactsAsync(order, kind, content, scheduledFor, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Notification failures never reverse the order operation.
        }
    }

    private async Task SendToActiveContactsAsync(
        Order order,
        NotificationKind kind,
        string content,
        DateTimeOffset? scheduledFor,
        CancellationToken cancellationToken)
    {
        var contacts = await _context.ContactNumbers
            .Where(x => x.BuyerId == order.BuyerId && x.DeletedAt == null)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);

        foreach (var contact in contacts)
        {
            var notification = new OrderNotification(
                order.Id,
                order.BuyerId,
                contact.Id,
                contact.CanonicalNumber,
                kind,
                content,
                DateTimeOffset.UtcNow,
                scheduledFor);
            _context.OrderNotifications.Add(notification);
            await _context.SaveChangesAsync(cancellationToken);
            await SendPersistedNotificationAsync(notification, cancellationToken);
        }
    }

    private async Task SendPersistedNotificationAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await _provider.SendAsync(
                notification.Destination,
                notification.Content!,
                notification.ScheduledFor,
                cancellationToken);
            notification.ApplyProviderSnapshot(snapshot, DateTimeOffset.UtcNow);
        }
        catch (SmsProviderException)
        {
            notification.MarkProviderFailure("Provider send failed or has an unknown outcome.", DateTimeOffset.UtcNow);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task TryCancelAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await _provider.CancelAsync(notification.ProviderMessageId!, cancellationToken);
            notification.ApplyProviderSnapshot(snapshot, DateTimeOffset.UtcNow);
            if (!string.Equals(notification.ProviderStatus, "canceled", StringComparison.OrdinalIgnoreCase))
            {
                notification.RequestCancellation();
            }
        }
        catch (SmsProviderException)
        {
            notification.RequestCancellation();
            notification.MarkProviderFailure("Provider cancellation will be retried.", DateTimeOffset.UtcNow);
        }
    }

    private static bool DidNotReachShopper(string status) => status is
        "failed" or "undelivered" or "canceled" or "provider_failure";

    private static bool IsCallerValidationError(HttpStatusCode? statusCode) =>
        statusCode is not null &&
        (int)statusCode >= 400 &&
        (int)statusCode < 500 &&
        statusCode is not HttpStatusCode.Unauthorized and not HttpStatusCode.Forbidden &&
        (int)statusCode != 429;
}
