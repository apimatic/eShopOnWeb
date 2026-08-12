using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.ApplicationCore.Sms;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : INotificationService
{
    // "a few days later" for the delivery follow-up.
    private static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<Notification> _notificationRepository;
    private readonly IReadRepository<ContactNumber> _contactNumberReadRepository;
    private readonly ISmsGateway _smsGateway;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Notification> notificationRepository,
        IReadRepository<ContactNumber> contactNumberReadRepository,
        ISmsGateway smsGateway,
        IAppLogger<OrderNotificationService> logger)
    {
        _notificationRepository = notificationRepository;
        _contactNumberReadRepository = contactNumberReadRepository;
        _smsGateway = smsGateway;
        _logger = logger;
    }

    public async Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        await SendForEventAsync(order, NotificationKind.OrderPlaced,
            $"eShop: thanks! Your order #{order.Id} has been placed.", cancellationToken);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        await SendForEventAsync(order, NotificationKind.OrderDispatched,
            $"eShop: good news — your order #{order.Id} is on its way!", cancellationToken);

        // Queue the "how did delivery go?" follow-up with the provider for a few days later.
        await ScheduleFollowUpAsync(order, cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        // Call off any not-yet-sent delivery follow-up first, so a cancelled order never gets asked
        // "how did delivery go?".
        await CancelPendingFollowUpAsync(order, cancellationToken);

        await SendForEventAsync(order, NotificationKind.OrderCancelled,
            $"eShop: your order #{order.Id} has been cancelled. Contact support if this is unexpected.",
            cancellationToken);
    }

    public async Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        // Idempotency: a repeat under the same key must not send a second message.
        var priorForKey = await _notificationRepository.FirstOrDefaultAsync(
            new NotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (priorForKey is not null)
        {
            _logger.LogInformation($"Resend replay for idempotency key; returning notification {priorForKey.Id}.");
            return ResendResult.Of(ResendOutcome.Replayed, priorForKey);
        }

        var original = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (original is null)
        {
            return ResendResult.Of(ResendOutcome.NotFound);
        }

        // A number the shopper has deleted must never be messaged again.
        var contact = await _contactNumberReadRepository.GetByIdAsync(original.ContactNumberId, cancellationToken);
        if (contact is null)
        {
            _logger.LogWarning($"Resend refused for notification {notificationId}: target contact number removed.");
            return ResendResult.Of(ResendOutcome.ContactRemoved);
        }

        // If the content was disposed of, there is nothing to re-send.
        if (original.ContentRedacted || string.IsNullOrEmpty(original.Body))
        {
            return ResendResult.Of(ResendOutcome.ContentDisposed);
        }

        var resend = new Notification(original.OrderId, original.BuyerId, original.Kind, contact.Id,
            original.ToNumber, original.Body!);
        resend.MarkAsResendOf(original.Id, idempotencyKey);

        await TrySendAsync(resend, original.ToNumber, original.Body!, cancellationToken);
        await _notificationRepository.AddAsync(resend, cancellationToken);

        _logger.LogInformation($"Resend of notification {notificationId} produced notification {resend.Id} (status {resend.Status}).");
        return ResendResult.Of(ResendOutcome.Created, resend);
    }

    public async Task DisposeContentAsync(Notification notification, CancellationToken cancellationToken = default)
    {
        // Remove the text at the provider first: if that fails we must not claim the content is gone.
        if (!string.IsNullOrEmpty(notification.ProviderMessageSid) && !notification.ContentRedacted)
        {
            await _smsGateway.RedactBodyAsync(notification.ProviderMessageSid!, cancellationToken);
        }

        notification.MarkContentRedacted();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
        _logger.LogInformation($"Disposed of content for notification {notification.Id}.");
    }

    public async Task RefreshDeliveryStateAsync(IEnumerable<Notification> notifications, CancellationToken cancellationToken = default)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid))
            {
                continue;
            }
            if (NotificationStatus.IsTerminal(notification.Status))
            {
                continue;
            }

            try
            {
                var current = await _smsGateway.FetchAsync(notification.ProviderMessageSid!, cancellationToken);
                notification.UpdateDeliveryState(current.Status, current.ErrorCode, current.ErrorMessage);
                await _notificationRepository.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                // Refresh is best-effort; keep the last-known state on failure. Never log PII.
                _logger.LogWarning($"Could not refresh delivery state for notification {notification.Id}: {ex.GetType().Name}.");
            }
        }
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var providerMessages = await _smsGateway.ListSentFromConfiguredNumberAsync(from, to, cancellationToken);

        var localWithSid = (await _notificationRepository.ListAsync(cancellationToken))
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .ToList();
        var localBySid = localWithSid
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();
        var providerSids = new HashSet<string>();

        foreach (var pm in providerMessages)
        {
            if (string.IsNullOrEmpty(pm.Sid))
            {
                continue;
            }
            providerSids.Add(pm.Sid);

            if (localBySid.TryGetValue(pm.Sid, out var local))
            {
                matched.Add(new ReconciliationEntry
                {
                    Sid = pm.Sid,
                    ProviderStatus = pm.Status,
                    DateSent = pm.DateSent,
                    NotificationId = local.Id,
                    OrderId = local.OrderId,
                    RecordedStatus = local.Status
                });
            }
            else
            {
                providerOnly.Add(new ReconciliationEntry
                {
                    Sid = pm.Sid,
                    ProviderStatus = pm.Status,
                    DateSent = pm.DateSent
                });
            }
        }

        // eShop believes it sent these (has a provider id, created in range) but the provider's record
        // for our number in this range doesn't include them.
        var eShopInRange = localWithSid.Where(n => n.CreatedDate >= from && n.CreatedDate <= to).ToList();
        var eShopOnly = eShopInRange
            .Where(n => !providerSids.Contains(n.ProviderMessageSid!))
            .Select(n => new ReconciliationEntry
            {
                Sid = n.ProviderMessageSid,
                NotificationId = n.Id,
                OrderId = n.OrderId,
                RecordedStatus = n.Status
            })
            .ToList();

        return new ReconciliationReport
        {
            From = from,
            To = to,
            Matched = matched,
            ProviderOnly = providerOnly,
            EShopOnly = eShopOnly,
            ProviderCount = providerMessages.Count,
            EShopCount = eShopInRange.Count
        };
    }

    private async Task<Notification?> SendForEventAsync(Order order, NotificationKind kind, string body,
        CancellationToken cancellationToken)
    {
        try
        {
            var contact = await GetLatestContactNumberAsync(order.BuyerId, cancellationToken);
            if (contact is null)
            {
                // A shopper with no number on file is simply not messaged.
                _logger.LogInformation($"Order {order.Id}: no contact number on file for {kind}; not messaging.");
                return null;
            }

            var notification = new Notification(order.Id, order.BuyerId, kind, contact.Id, contact.PhoneNumber, body);
            await TrySendAsync(notification, contact.PhoneNumber, body, cancellationToken);
            await _notificationRepository.AddAsync(notification, cancellationToken);
            return notification;
        }
        catch (Exception ex)
        {
            // A message that cannot be sent must never fail the underlying operation.
            _logger.LogWarning($"Order {order.Id}: unexpected error producing {kind} notification: {ex.GetType().Name}.");
            return null;
        }
    }

    private async Task ScheduleFollowUpAsync(Order order, CancellationToken cancellationToken)
    {
        try
        {
            var contact = await GetLatestContactNumberAsync(order.BuyerId, cancellationToken);
            if (contact is null)
            {
                return;
            }

            var sendAt = DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay);
            var body = $"eShop: how did the delivery of your order #{order.Id} go? Reply to let us know.";
            var notification = new Notification(order.Id, order.BuyerId, NotificationKind.DeliveryFollowUp,
                contact.Id, contact.PhoneNumber, body);

            try
            {
                var scheduled = await _smsGateway.ScheduleAsync(contact.PhoneNumber, body, sendAt, cancellationToken);
                notification.RecordProviderResult(scheduled.Sid, scheduled.Status, scheduled.ErrorCode,
                    scheduled.ErrorMessage, sendAt);
            }
            catch (Exception ex)
            {
                notification.RecordSendFailure(SafeError(ex));
                _logger.LogWarning($"Order {order.Id}: could not schedule delivery follow-up: {SafeError(ex)}.");
            }

            await _notificationRepository.AddAsync(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Order {order.Id}: unexpected error scheduling follow-up: {ex.GetType().Name}.");
        }
    }

    private async Task CancelPendingFollowUpAsync(Order order, CancellationToken cancellationToken)
    {
        try
        {
            var notifications = await _notificationRepository.ListAsync(
                new NotificationsByOrderSpecification(order.Id), cancellationToken);

            foreach (var followUp in notifications.Where(n =>
                         n.Kind == NotificationKind.DeliveryFollowUp &&
                         !string.IsNullOrEmpty(n.ProviderMessageSid) &&
                         !NotificationStatus.IsTerminal(n.Status)))
            {
                try
                {
                    var cancelled = await _smsGateway.CancelAsync(followUp.ProviderMessageSid!, cancellationToken);
                    followUp.UpdateDeliveryState(cancelled.Status, cancelled.ErrorCode, cancelled.ErrorMessage);
                    await _notificationRepository.UpdateAsync(followUp, cancellationToken);
                    _logger.LogInformation($"Order {order.Id}: delivery follow-up {followUp.Id} cancelled (status {followUp.Status}).");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Order {order.Id}: could not cancel follow-up {followUp.Id}: {SafeError(ex)}.");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Order {order.Id}: unexpected error cancelling follow-up: {ex.GetType().Name}.");
        }
    }

    private async Task TrySendAsync(Notification notification, string toNumber, string body, CancellationToken cancellationToken)
    {
        try
        {
            var sent = await _smsGateway.SendAsync(toNumber, body, cancellationToken);
            notification.RecordProviderResult(sent.Sid, sent.Status, sent.ErrorCode, sent.ErrorMessage);
        }
        catch (Exception ex)
        {
            notification.RecordSendFailure(SafeError(ex));
            _logger.LogWarning($"Could not send {notification.Kind} message: {SafeError(ex)}.");
        }
    }

    private async Task<ContactNumber?> GetLatestContactNumberAsync(string buyerId, CancellationToken cancellationToken)
    {
        return await _contactNumberReadRepository.FirstOrDefaultAsync(
            new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
    }

    // A log-safe error string that never includes provider message text (which can carry a number).
    private static string SafeError(Exception ex) =>
        ex is ISafeLoggableException safe ? safe.SafeSummary : ex.GetType().Name;
}
