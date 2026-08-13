using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates the SMS notifications that go out as an order moves, and the operator actions on them.
/// A failure to send never propagates out of the "notify" methods: it is recorded on the notification and the
/// caller's business operation still succeeds. A shopper with no number on file is simply not messaged.
/// </summary>
public class OrderNotificationService : IOrderNotificationService
{
    /// <summary>How long after dispatch the "how did the delivery go?" follow-up is scheduled for.</summary>
    public static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<Notification> _notificationRepository;
    private readonly IReadRepository<ContactNumber> _contactNumberRepository;
    private readonly IRepository<ResendIdempotencyRecord> _idempotencyRepository;
    private readonly ISmsGateway _smsGateway;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Notification> notificationRepository,
        IReadRepository<ContactNumber> contactNumberRepository,
        IRepository<ResendIdempotencyRecord> idempotencyRepository,
        ISmsGateway smsGateway,
        IAppLogger<OrderNotificationService> logger)
    {
        _notificationRepository = notificationRepository;
        _contactNumberRepository = contactNumberRepository;
        _idempotencyRepository = idempotencyRepository;
        _smsGateway = smsGateway;
        _logger = logger;
    }

    public async Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        foreach (var number in await GetBuyerNumbersAsync(order.BuyerId, cancellationToken))
        {
            await SendAndRecordAsync(order, NotificationKind.OrderPlaced, PlacedBody(order), number.PhoneNumber, cancellationToken);
        }
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        foreach (var number in await GetBuyerNumbersAsync(order.BuyerId, cancellationToken))
        {
            await SendAndRecordAsync(order, NotificationKind.OrderDispatched, DispatchedBody(order), number.PhoneNumber, cancellationToken);
            await ScheduleFollowUpAsync(order, FollowUpBody(order), number.PhoneNumber, cancellationToken);
        }
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        // First call off any follow-up that has not yet gone out, so a cancelled order never prompts a
        // "how did your delivery go?" message.
        var scheduled = await _notificationRepository.ListAsync(new ScheduledFollowUpsByOrderSpecification(order.Id), cancellationToken);
        foreach (var followUp in scheduled)
        {
            try
            {
                var state = await _smsGateway.CancelAsync(followUp.ProviderMessageSid!, cancellationToken);
                followUp.UpdateProviderStatus(state.Status, state.ErrorCode, state.ErrorMessage);
                await _notificationRepository.UpdateAsync(followUp, cancellationToken);
            }
            catch (Exception ex)
            {
                // The cancel could not be confirmed; surface it in logs but do not fail the cancellation of the
                // order itself. The next status refresh will reflect the provider's actual state.
                _logger.LogWarning($"Could not cancel scheduled follow-up notification {followUp.Id} for order {order.Id}: {Describe(ex)}");
            }
        }

        foreach (var number in await GetBuyerNumbersAsync(order.BuyerId, cancellationToken))
        {
            await SendAndRecordAsync(order, NotificationKind.OrderCancelled, CancelledBody(order), number.PhoneNumber, cancellationToken);
        }
    }

    public async Task<ResendOutcome> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new ArgumentException("An idempotency key is required.", nameof(idempotencyKey));

        // Repeating a request under the same key must not send a second message.
        var priorRequest = await _idempotencyRepository.FirstOrDefaultAsync(new ResendIdempotencyByKeySpecification(idempotencyKey), cancellationToken);
        if (priorRequest is not null)
            return new ResendOutcome(priorRequest.ResultNotificationId, true);

        var source = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new NotificationNotFoundException(notificationId);

        if (source.Body is null)
            throw new NotificationContentUnavailableException(notificationId);

        var resend = new Notification(source.OrderId, source.BuyerId, NotificationKind.Resend, source.ToNumber, source.Body);
        try
        {
            var result = await _smsGateway.SendAsync(source.ToNumber, source.Body, cancellationToken);
            resend.RecordProviderAccepted(result.ProviderMessageSid, result.Status);
        }
        catch (Exception ex)
        {
            resend.RecordLocalFailure(Describe(ex));
            _logger.LogWarning($"Resend of notification {notificationId} could not be handed to the provider: {Describe(ex)}");
        }

        resend = await _notificationRepository.AddAsync(resend, cancellationToken);
        await _idempotencyRepository.AddAsync(new ResendIdempotencyRecord(idempotencyKey, source.Id, resend.Id), cancellationToken);

        return new ResendOutcome(resend.Id, false);
    }

    public async Task DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new NotificationNotFoundException(notificationId);

        if (notification.ContentRedacted)
            return;

        // Dispose of the content at the provider first, so it is truly no longer retrievable — not merely hidden
        // here. If the provider call fails, the exception propagates and nothing is marked locally.
        if (notification.ProviderMessageSid is not null)
            await _smsGateway.RedactContentAsync(notification.ProviderMessageSid, cancellationToken);

        notification.MarkContentRedacted();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
    }

    public async Task<IReadOnlyList<Notification>> GetOrderNotificationsAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderSpecification(orderId), cancellationToken);
        await RefreshStatusesAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task RefreshStatusesAsync(IReadOnlyList<Notification> notifications, CancellationToken cancellationToken = default)
    {
        foreach (var notification in notifications)
        {
            if (notification.ProviderMessageSid is null || notification.IsProviderStatusTerminal)
                continue;

            try
            {
                var state = await _smsGateway.GetStatusAsync(notification.ProviderMessageSid, cancellationToken);
                notification.UpdateProviderStatus(state.Status, state.ErrorCode, state.ErrorMessage);
                await _notificationRepository.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                // A read of provider state must never fail the caller's request.
                _logger.LogWarning($"Could not refresh provider status for notification {notification.Id}: {Describe(ex)}");
            }
        }
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var providerMessages = await _smsGateway.ListSentMessagesAsync(from, to, cancellationToken);
        var eShopNotifications = await _notificationRepository.ListAsync(new NotificationsWithProviderSidBetweenSpecification(from, to), cancellationToken);

        var eShopBySid = eShopNotifications
            .Where(n => n.ProviderMessageSid is not null)
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var providerSids = new HashSet<string>();
        var entries = new List<ReconciliationEntry>();
        int matched = 0, providerOnly = 0;

        foreach (var message in providerMessages)
        {
            if (string.IsNullOrEmpty(message.Sid) || !providerSids.Add(message.Sid))
                continue;

            if (eShopBySid.TryGetValue(message.Sid, out var notification))
            {
                matched++;
                entries.Add(new ReconciliationEntry(
                    message.Sid, ReconciliationMatch.Matched, message.Status, message.ErrorCode,
                    notification.Id, notification.OrderId, notification.EffectiveStatus,
                    Mask(message.To), message.DateSent));
            }
            else
            {
                providerOnly++;
                entries.Add(new ReconciliationEntry(
                    message.Sid, ReconciliationMatch.ProviderOnly, message.Status, message.ErrorCode,
                    null, null, null, Mask(message.To), message.DateSent));
            }
        }

        int eShopOnly = 0;
        foreach (var notification in eShopNotifications)
        {
            if (notification.ProviderMessageSid is null || providerSids.Contains(notification.ProviderMessageSid))
                continue;

            eShopOnly++;
            entries.Add(new ReconciliationEntry(
                notification.ProviderMessageSid, ReconciliationMatch.EShopOnly, null, notification.ProviderErrorCode,
                notification.Id, notification.OrderId, notification.EffectiveStatus,
                Mask(notification.ToNumber), null));
        }

        return new ReconciliationReport(
            from, to,
            providerSids.Count,
            eShopNotifications.Count(n => n.ProviderMessageSid is not null),
            matched, providerOnly, eShopOnly,
            entries);
    }

    private async Task<IReadOnlyList<ContactNumber>> GetBuyerNumbersAsync(string buyerId, CancellationToken cancellationToken)
    {
        return await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
    }

    private async Task SendAndRecordAsync(Order order, NotificationKind kind, string body, string toNumber, CancellationToken cancellationToken)
    {
        var notification = new Notification(order.Id, order.BuyerId, kind, toNumber, body);
        try
        {
            var result = await _smsGateway.SendAsync(toNumber, body, cancellationToken);
            notification.RecordProviderAccepted(result.ProviderMessageSid, result.Status);
        }
        catch (Exception ex)
        {
            notification.RecordLocalFailure(Describe(ex));
            _logger.LogWarning($"Could not send {kind} notification for order {order.Id}: {Describe(ex)}");
        }

        await _notificationRepository.AddAsync(notification, cancellationToken);
    }

    private async Task ScheduleFollowUpAsync(Order order, string body, string toNumber, CancellationToken cancellationToken)
    {
        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        var notification = new Notification(order.Id, order.BuyerId, NotificationKind.DeliveryFollowUp, toNumber, body, sendAt);
        try
        {
            var result = await _smsGateway.ScheduleAsync(toNumber, body, sendAt, cancellationToken);
            notification.RecordProviderAccepted(result.ProviderMessageSid, result.Status);
        }
        catch (Exception ex)
        {
            notification.RecordLocalFailure(Describe(ex));
            _logger.LogWarning($"Could not schedule delivery follow-up for order {order.Id}: {Describe(ex)}");
        }

        await _notificationRepository.AddAsync(notification, cancellationToken);
    }

    private static string PlacedBody(Order order) =>
        $"eShop: thanks! Your order #{order.Id} has been placed. We'll text you when it ships.";

    private static string DispatchedBody(Order order) =>
        $"eShop: good news - your order #{order.Id} is on its way!";

    private static string FollowUpBody(Order order) =>
        $"eShop: how did the delivery of your order #{order.Id} go? We'd love your feedback.";

    private static string CancelledBody(Order order) =>
        $"eShop: your order #{order.Id} has been cancelled. Get in touch if this is unexpected.";

    /// <summary>A PII-free description of an exception for logging (never includes credentials or recipient numbers).</summary>
    private static string Describe(Exception ex) => $"{ex.GetType().Name}: {ex.Message}";

    /// <summary>Masks a phone number so only its last four digits remain, for display in operator reports.</summary>
    private static string? Mask(string? number)
    {
        if (string.IsNullOrEmpty(number))
            return number;
        if (number.Length <= 4)
            return new string('*', number.Length);
        return string.Concat(new string('*', number.Length - 4), number.AsSpan(number.Length - 4));
    }
}
