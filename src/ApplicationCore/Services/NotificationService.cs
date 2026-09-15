using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Sends order notifications by SMS and carries out the operator actions on them. All provider I/O is
/// isolated so that a message which cannot be sent records its outcome on the notification and never
/// fails the order operation that triggered it.
/// </summary>
public class NotificationService : INotificationService
{
    // "A few days later" for the post-delivery follow-up. Comfortably inside the provider's
    // 15-minutes-to-35-days scheduling window.
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    // Serialises the check-then-create of re-send idempotency keys so a repeated key can never race
    // two messages out. Static so it is shared across this service's (scoped) instances.
    private static readonly SemaphoreSlim ResendGate = new(1, 1);

    private readonly IRepository<Notification> _notifications;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IMessagingProvider _provider;
    private readonly IAppLogger<NotificationService> _logger;

    public NotificationService(
        IRepository<Notification> notifications,
        IRepository<ContactNumber> contactNumbers,
        IMessagingProvider provider,
        IAppLogger<NotificationService> logger)
    {
        _notifications = notifications;
        _contactNumbers = contactNumbers;
        _provider = provider;
        _logger = logger;
    }

    public async Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"eShop: your order #{order.Id} has been placed. Thank you for shopping with us!";
        foreach (var number in await NumbersFor(order.BuyerId, cancellationToken))
        {
            await SendImmediateAsync(order, NotificationKind.OrderPlaced, number, body, cancellationToken);
        }
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var dispatchBody = $"eShop: good news — your order #{order.Id} is on its way!";
        var followUpBody = $"eShop: how did the delivery of your order #{order.Id} go? We'd love your feedback.";
        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);

        foreach (var number in await NumbersFor(order.BuyerId, cancellationToken))
        {
            await SendImmediateAsync(order, NotificationKind.OrderDispatched, number, dispatchBody, cancellationToken);
            await ScheduleFollowUpAsync(order, number, followUpBody, sendAt, cancellationToken);
        }
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"eShop: your order #{order.Id} has been cancelled. If this is unexpected, please contact us.";
        foreach (var number in await NumbersFor(order.BuyerId, cancellationToken))
        {
            await SendImmediateAsync(order, NotificationKind.OrderCancelled, number, body, cancellationToken);
        }

        // Call off any delivery follow-up still scheduled with the provider so it never reaches the
        // shopper — done regardless of whether the shopper still has a number on file.
        var pending = await _notifications.ListAsync(new PendingFollowUpsByOrderSpecification(order.Id), cancellationToken);
        foreach (var followUp in pending)
        {
            await CancelFollowUpAsync(followUp, cancellationToken);
        }
    }

    public async Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var source = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (source is null)
        {
            return new ResendResult(ResendStatus.NotFound, null);
        }

        if (source.ContentDisposed || string.IsNullOrEmpty(source.Body))
        {
            // Nothing to re-send once the content has been disposed of.
            return new ResendResult(ResendStatus.CannotResend, null);
        }

        await ResendGate.WaitAsync(cancellationToken);
        try
        {
            var existing = await _notifications.FirstOrDefaultAsync(
                new NotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
            if (existing is not null)
            {
                // Same key already handled — return the original result, send nothing new.
                return new ResendResult(ResendStatus.Duplicate, existing.Id);
            }

            var resend = new Notification(source.OrderId, source.BuyerId, NotificationKind.Resend, source.ToNumber, source.Body);
            resend.SetIdempotencyKey(idempotencyKey);
            // Persist with the key registered before sending, so a concurrent repeat sees it as handled.
            await _notifications.AddAsync(resend, cancellationToken);

            await TrySendAsync(resend, cancellationToken);
            await _notifications.UpdateAsync(resend, cancellationToken);

            return new ResendResult(ResendStatus.Created, resend.Id);
        }
        finally
        {
            ResendGate.Release();
        }
    }

    public async Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            return false;
        }

        if (notification.ContentDisposed)
        {
            return true; // already disposed; idempotent
        }

        // Remove the content at the provider first; only mark it disposed once that has actually
        // happened, so we never claim disposal the provider didn't perform. A failure surfaces.
        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            await _provider.RedactContentAsync(notification.ProviderMessageSid!, cancellationToken);
        }

        notification.MarkContentDisposed();
        await _notifications.UpdateAsync(notification, cancellationToken);
        _logger.LogInformation("Disposed content of notification {0} (message {1}).",
            notification.Id, notification.ProviderMessageSid ?? "<none>");
        return true;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var providerRecords = await _provider.ListSentMessagesAsync(from, to, cancellationToken);
        var eShopRecords = await _notifications.ListAsync(new SentNotificationsInRangeSpecification(from, to), cancellationToken);

        var providerBySid = providerRecords
            .Where(r => !string.IsNullOrEmpty(r.Sid))
            .GroupBy(r => r.Sid)
            .ToDictionary(g => g.Key, g => g.First());
        var eShopBySid = eShopRecords
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();
        var eShopOnly = new List<ReconciliationEntry>();

        foreach (var (sid, record) in providerBySid)
        {
            if (eShopBySid.TryGetValue(sid, out var notification))
            {
                matched.Add(new ReconciliationEntry(sid, notification.Id, record.Status, notification.ProviderStatus));
            }
            else
            {
                providerOnly.Add(new ReconciliationEntry(sid, null, record.Status, null));
            }
        }

        foreach (var (sid, notification) in eShopBySid)
        {
            if (!providerBySid.ContainsKey(sid))
            {
                eShopOnly.Add(new ReconciliationEntry(sid, notification.Id, null, notification.ProviderStatus));
            }
        }

        return new ReconciliationReport(from, to,
            providerBySid.Count, eShopBySid.Count, matched.Count,
            matched, providerOnly, eShopOnly);
    }

    public async Task RefreshStatusesAsync(IReadOnlyCollection<Notification> notifications, CancellationToken cancellationToken = default)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid) || notification.IsTerminal() || notification.ContentDisposed)
            {
                continue;
            }

            try
            {
                var state = await _provider.FetchAsync(notification.ProviderMessageSid!, cancellationToken);
                notification.UpdateProviderState(state.Status, state.ErrorCode);
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                // A read failure must not fail the caller's request; report on last known state.
                _logger.LogWarning("Could not refresh status of notification {0} (message {1}): {2}",
                    notification.Id, notification.ProviderMessageSid!, ex.Message);
            }
        }
    }

    private async Task<IReadOnlyList<ContactNumber>> NumbersFor(string buyerId, CancellationToken cancellationToken)
    {
        return await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
    }

    private async Task SendImmediateAsync(Order order, NotificationKind kind, ContactNumber number, string body, CancellationToken cancellationToken)
    {
        var notification = new Notification(order.Id, order.BuyerId, kind, number.PhoneNumber, body);
        await _notifications.AddAsync(notification, cancellationToken);
        await TrySendAsync(notification, cancellationToken);
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    private async Task ScheduleFollowUpAsync(Order order, ContactNumber number, string body, DateTimeOffset sendAt, CancellationToken cancellationToken)
    {
        var notification = new Notification(order.Id, order.BuyerId, NotificationKind.DeliveryFollowUp, number.PhoneNumber, body);
        notification.SetScheduledSendAt(sendAt);
        await _notifications.AddAsync(notification, cancellationToken);
        try
        {
            var result = await _provider.ScheduleAsync(number.PhoneNumber, body, sendAt, cancellationToken);
            notification.SetProviderResult(result.Sid, result.Status, result.ErrorCode);
        }
        catch (Exception ex)
        {
            notification.MarkSendFailed();
            _logger.LogWarning("Failed to schedule delivery follow-up for order {0} (notification {1}): {2}",
                order.Id, notification.Id, ex.Message);
        }
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    private async Task CancelFollowUpAsync(Notification followUp, CancellationToken cancellationToken)
    {
        try
        {
            if (!string.IsNullOrEmpty(followUp.ProviderMessageSid))
            {
                await _provider.CancelScheduledAsync(followUp.ProviderMessageSid!, cancellationToken);
            }
            followUp.UpdateProviderState("canceled", null);
            await _notifications.UpdateAsync(followUp, cancellationToken);
            _logger.LogInformation("Cancelled delivery follow-up (notification {0}, message {1}) for order {2}.",
                followUp.Id, followUp.ProviderMessageSid ?? "<none>", followUp.OrderId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to cancel delivery follow-up (notification {0}, message {1}): {2}",
                followUp.Id, followUp.ProviderMessageSid ?? "<none>", ex.Message);
        }
    }

    /// <summary>Sends immediately and records the outcome. Never throws — a failed send is an outcome.</summary>
    private async Task TrySendAsync(Notification notification, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _provider.SendAsync(notification.ToNumber, notification.Body!, cancellationToken);
            notification.SetProviderResult(result.Sid, result.Status, result.ErrorCode);
        }
        catch (Exception ex)
        {
            notification.MarkSendFailed();
            _logger.LogWarning("Failed to send {0} notification {1} for order {2}: {3}",
                notification.Kind, notification.Id, notification.OrderId, ex.Message);
        }
    }
}
