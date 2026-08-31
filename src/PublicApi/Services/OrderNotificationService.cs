using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.Twilio;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Services;

public interface IOrderNotificationService
{
    /// <summary>Tells the shopper their order was placed. Never throws; no-op without a number on file.</summary>
    Task NotifyOrderPlacedAsync(Order order, CancellationToken ct);

    /// <summary>Tells the shopper the order is on its way and queues the delivery follow-up with the provider. Never throws.</summary>
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken ct);

    /// <summary>Tells the shopper the order was cancelled and calls off any not-yet-sent follow-up at the provider. Never throws.</summary>
    Task NotifyOrderCancelledAsync(Order order, CancellationToken ct);

    /// <summary>Refreshes non-terminal delivery states by asking the provider. Failures degrade to last-known state.</summary>
    Task RefreshDeliveryStatesAsync(IEnumerable<OrderNotification> notifications, CancellationToken ct);

    Task<ResendNotificationResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct);

    Task<RedactNotificationContentResult> RedactContentAsync(int notificationId, CancellationToken ct);

    Task<ReconciliationReport> GetReconciliationAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}

public enum ResendNotificationStatus
{
    Resent,
    Duplicate,
    NotificationNotFound,
    ContentDisposed,
    IdempotencyKeyConflict
}

public sealed record ResendNotificationResult(ResendNotificationStatus Status, OrderNotification? Notification);

public enum RedactNotificationContentStatus
{
    Redacted,
    NotificationNotFound
}

public sealed record RedactNotificationContentResult(RedactNotificationContentStatus Status);

public sealed record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    bool Truncated,
    IReadOnlyList<ReconciliationMatch> Matched,
    IReadOnlyList<ReconciliationProviderEntry> ProviderOnly,
    IReadOnlyList<ReconciliationAppEntry> AppOnly)
{
    public int ProviderMessageCount => Matched.Count + ProviderOnly.Count;
    public int AppNotificationCount => Matched.Count + AppOnly.Count;
}

public sealed record ReconciliationMatch(
    string ProviderMessageSid,
    int NotificationId,
    string? ProviderStatus,
    string? AppStatus,
    bool StatusAgrees);

public sealed record ReconciliationProviderEntry(
    string ProviderMessageSid,
    string? To,
    string? Status,
    int? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset? DateSent);

public sealed record ReconciliationAppEntry(
    int NotificationId,
    int OrderId,
    NotificationKind Kind,
    string? ProviderMessageSid,
    string? Status,
    DateTimeOffset CreatedAt);

public class OrderNotificationService : IOrderNotificationService
{
    // Provider-side scheduling window is enforced app-side; 3 days sits well inside it.
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly ITwilioMessaging _twilioMessaging;
    private readonly IOptions<TwilioOptions> _options;
    private readonly ILogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<OrderNotification> notificationRepository,
        IRepository<ContactNumber> contactNumberRepository,
        ITwilioMessaging twilioMessaging,
        IOptions<TwilioOptions> options,
        ILogger<OrderNotificationService> logger)
    {
        _notificationRepository = notificationRepository;
        _contactNumberRepository = contactNumberRepository;
        _twilioMessaging = twilioMessaging;
        _options = options;
        _logger = logger;
    }

    public async Task NotifyOrderPlacedAsync(Order order, CancellationToken ct)
    {
        var contactNumber = await GetCurrentContactNumberAsync(order.BuyerId, ct);
        if (contactNumber is null)
        {
            return;
        }

        var notification = new OrderNotification(
            order.Id, order.BuyerId, NotificationKind.OrderPlaced, contactNumber.PhoneNumber, NotificationText.OrderPlaced(order));
        await SendAndRecordAsync(notification, ct);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken ct)
    {
        var contactNumber = await GetCurrentContactNumberAsync(order.BuyerId, ct);
        if (contactNumber is null)
        {
            return;
        }

        var dispatched = new OrderNotification(
            order.Id, order.BuyerId, NotificationKind.OrderDispatched, contactNumber.PhoneNumber, NotificationText.OrderDispatched(order));
        await SendAndRecordAsync(dispatched, ct);

        // The follow-up is queued with the provider itself — nothing in this app sends it later.
        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        var followUp = new OrderNotification(
            order.Id, order.BuyerId, NotificationKind.DeliveryFollowUp, contactNumber.PhoneNumber, NotificationText.DeliveryFollowUp(order), sendAt);
        try
        {
            var scheduled = await _twilioMessaging.ScheduleMessageAsync(followUp.ToNumber, followUp.Body!, sendAt, ct);
            followUp.MarkAccepted(scheduled.Sid, scheduled.Status);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not queue the delivery follow-up for order {OrderId} with the provider.", order.Id);
            followUp.MarkSendFailed(Describe(ex));
        }
        await _notificationRepository.AddAsync(followUp, ct);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken ct)
    {
        var contactNumber = await GetCurrentContactNumberAsync(order.BuyerId, ct);
        if (contactNumber is not null)
        {
            var notification = new OrderNotification(
                order.Id, order.BuyerId, NotificationKind.OrderCancelled, contactNumber.PhoneNumber, NotificationText.OrderCancelled(order));
            await SendAndRecordAsync(notification, ct);
        }

        // A follow-up that has not gone out yet must never reach the shopper.
        var pendingFollowUps = await _notificationRepository.ListAsync(
            new CancellableFollowUpsForOrderSpecification(order.Id), ct);
        foreach (var followUp in pendingFollowUps)
        {
            try
            {
                var cancelled = await _twilioMessaging.CancelScheduledMessageAsync(followUp.ProviderMessageSid!, ct);
                followUp.UpdateDeliveryState(cancelled.Status, cancelled.ErrorCode, cancelled.ErrorMessage);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not call off scheduled follow-up {NotificationId} for order {OrderId} at the provider.",
                    followUp.Id, order.Id);
                followUp.UpdateDeliveryState(followUp.Status, null, "Provider cancellation of the scheduled message failed.");
            }
            await _notificationRepository.UpdateAsync(followUp, ct);
        }
    }

    public async Task RefreshDeliveryStatesAsync(IEnumerable<OrderNotification> notifications, CancellationToken ct)
    {
        foreach (var notification in notifications.Where(n => n.ProviderMessageSid is not null && !n.IsInTerminalState))
        {
            try
            {
                var message = await _twilioMessaging.FetchMessageAsync(notification.ProviderMessageSid!, ct);
                if (message is null)
                {
                    notification.UpdateDeliveryState(notification.Status, null, "The provider no longer holds this message.");
                }
                else
                {
                    notification.UpdateDeliveryState(message.Status, message.ErrorCode, message.ErrorMessage);
                }
                await _notificationRepository.UpdateAsync(notification, ct);
            }
            catch (Exception ex)
            {
                // Status reporting must not fail the read: serve the last known state.
                _logger.LogWarning(ex, "Could not refresh delivery state for notification {NotificationId}.", notification.Id);
            }
        }
    }

    public async Task<ResendNotificationResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct)
    {
        // Dedupe before any provider call: a repeated key must not send a second message.
        var existing = await _notificationRepository.FirstOrDefaultAsync(
            new NotificationByIdempotencyKeySpecification(idempotencyKey), ct);
        if (existing is not null)
        {
            return new ResendNotificationResult(
                existing.ResendOfNotificationId == notificationId
                    ? ResendNotificationStatus.Duplicate
                    : ResendNotificationStatus.IdempotencyKeyConflict,
                existing.ResendOfNotificationId == notificationId ? existing : null);
        }

        var original = await _notificationRepository.GetByIdAsync(notificationId, ct);
        if (original is null)
        {
            return new ResendNotificationResult(ResendNotificationStatus.NotificationNotFound, null);
        }

        if (original.ContentRedacted || original.Body is null)
        {
            return new ResendNotificationResult(ResendNotificationStatus.ContentDisposed, null);
        }

        var resend = new OrderNotification(original.OrderId, original.BuyerId, original.Kind, original.ToNumber, original.Body);
        resend.MarkAsResend(original.Id, idempotencyKey);

        try
        {
            var message = await _twilioMessaging.SendMessageAsync(resend.ToNumber, resend.Body!, ct);
            resend.MarkAccepted(message.Sid, message.Status);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Resend of notification {NotificationId} failed at the provider.", notificationId);
            resend.MarkSendFailed(Describe(ex));
        }

        try
        {
            await _notificationRepository.AddAsync(resend, ct);
        }
        catch (DbUpdateException)
        {
            // Lost a concurrent race on the idempotency key (unique index): the other request's row wins.
            var winner = await _notificationRepository.FirstOrDefaultAsync(
                new NotificationByIdempotencyKeySpecification(idempotencyKey), ct);
            if (winner is not null && winner.ResendOfNotificationId == notificationId)
            {
                return new ResendNotificationResult(ResendNotificationStatus.Duplicate, winner);
            }
            throw;
        }

        return new ResendNotificationResult(ResendNotificationStatus.Resent, resend);
    }

    public async Task<RedactNotificationContentResult> RedactContentAsync(int notificationId, CancellationToken ct)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, ct);
        if (notification is null)
        {
            return new RedactNotificationContentResult(RedactNotificationContentStatus.NotificationNotFound);
        }

        if (!notification.ContentRedacted && notification.ProviderMessageSid is not null)
        {
            try
            {
                // Erase the body at the provider too — not merely hide it in this app.
                await _twilioMessaging.RedactMessageBodyAsync(notification.ProviderMessageSid, ct);
            }
            catch (TwilioProviderException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                // The provider record is already gone, so its content is no longer retrievable there either.
            }
        }

        notification.MarkContentRedacted();
        await _notificationRepository.UpdateAsync(notification, ct);
        return new RedactNotificationContentResult(RedactNotificationContentStatus.Redacted);
    }

    public async Task<ReconciliationReport> GetReconciliationAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var providerList = await _twilioMessaging.ListMessagesFromSenderAsync(from, to, ct);
        var appNotifications = await _notificationRepository.ListAsync(
            new NotificationsInDateRangeSpecification(from, to), ct);

        var appBySid = appNotifications
            .Where(n => n.ProviderMessageSid is not null)
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new List<ReconciliationMatch>();
        var providerOnly = new List<ReconciliationProviderEntry>();
        var providerSids = new HashSet<string>();

        foreach (var message in providerList.Messages)
        {
            providerSids.Add(message.Sid);
            if (appBySid.TryGetValue(message.Sid, out var notification))
            {
                matched.Add(new ReconciliationMatch(
                    message.Sid,
                    notification.Id,
                    message.Status,
                    notification.Status,
                    string.Equals(message.Status, notification.Status, StringComparison.OrdinalIgnoreCase)));
            }
            else
            {
                providerOnly.Add(new ReconciliationProviderEntry(
                    message.Sid, message.To, message.Status, message.ErrorCode, message.ErrorMessage, message.DateSent));
            }
        }

        var appOnly = appNotifications
            .Where(n => n.ProviderMessageSid is null || !providerSids.Contains(n.ProviderMessageSid))
            .Select(n => new ReconciliationAppEntry(n.Id, n.OrderId, n.Kind, n.ProviderMessageSid, n.Status, n.CreatedAt))
            .ToList();

        return new ReconciliationReport(from, to, _options.Value.FromNumber, providerList.Truncated, matched, providerOnly, appOnly);
    }

    private async Task SendAndRecordAsync(OrderNotification notification, CancellationToken ct)
    {
        try
        {
            var message = await _twilioMessaging.SendMessageAsync(notification.ToNumber, notification.Body!, ct);
            notification.MarkAccepted(message.Sid, message.Status);
        }
        catch (Exception ex)
        {
            // A message that cannot be sent must never fail the underlying operation.
            _logger.LogWarning(ex, "Could not send {Kind} notification for order {OrderId}.", notification.Kind, notification.OrderId);
            notification.MarkSendFailed(Describe(ex));
        }

        await _notificationRepository.AddAsync(notification, ct);
    }

    private async Task<ContactNumber?> GetCurrentContactNumberAsync(string buyerId, CancellationToken ct)
    {
        // The shopper's most recently registered number is their current one.
        var numbers = await _contactNumberRepository.ListAsync(new ContactNumbersForBuyerSpecification(buyerId), ct);
        return numbers.FirstOrDefault();
    }

    private static string Describe(Exception ex) => ex switch
    {
        TwilioProviderException provider when provider.StatusCode is not null =>
            $"Provider rejected the send (HTTP {(int)provider.StatusCode}, error code {provider.ProviderErrorCode?.ToString() ?? "unknown"}).",
        TwilioProviderException => "The messaging provider could not be reached.",
        _ => "The notification could not be sent."
    };
}
