using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Raises and tracks the messages that go out as an order moves. All "notify" operations are
/// best-effort: a message that cannot be sent is recorded but never fails the order operation, and a
/// shopper with no number on file is simply not messaged. No shopper phone number is ever logged.
/// </summary>
public class OrderNotificationService : IOrderNotificationService
{
    // Provider delivery outcomes that are final: no point refreshing them again.
    private static readonly HashSet<string> TerminalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "delivered", "undelivered", "failed", "canceled", "received", "read"
    };

    // Matches phone-number-like runs so provider-sourced text can be scrubbed of any number before it is
    // stored or surfaced.
    private static readonly Regex PhoneLikePattern = new(@"\+?\d[\d\-\s().]{6,}\d", RegexOptions.Compiled);

    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IReadRepository<ContactNumber> _contactNumberRepository;
    private readonly ISmsSender _smsSender;
    private readonly OrderNotificationOptions _options;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<OrderNotification> notificationRepository,
        IReadRepository<ContactNumber> contactNumberRepository,
        ISmsSender smsSender,
        OrderNotificationOptions options,
        IAppLogger<OrderNotificationService> logger)
    {
        _notificationRepository = notificationRepository;
        _contactNumberRepository = contactNumberRepository;
        _smsSender = smsSender;
        _options = options;
        _logger = logger;
    }

    public async Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(order, nameof(order));
        var recipient = await ResolveRecipientAsync(order.BuyerId, cancellationToken);
        var body = $"eShopOnWeb: thanks! Your order #{order.Id} has been placed.";
        await SendAndRecordAsync(order, NotificationKind.OrderPlaced, body, recipient, scheduleFor: null, cancellationToken);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(order, nameof(order));
        var recipient = await ResolveRecipientAsync(order.BuyerId, cancellationToken);

        var dispatchedBody = $"eShopOnWeb: good news — your order #{order.Id} is on its way!";
        await SendAndRecordAsync(order, NotificationKind.OrderDispatched, dispatchedBody, recipient, scheduleFor: null, cancellationToken);

        // Queue a "how did the delivery go?" follow-up with the provider for a few days later — the app
        // does not hold it to send by a timer of its own.
        var followUpBody = $"eShopOnWeb: how did the delivery of your order #{order.Id} go? We'd love your feedback.";
        var sendAt = DateTimeOffset.UtcNow.Add(_options.DeliveryFollowUpDelay);
        await SendAndRecordAsync(order, NotificationKind.DeliveryFollowUp, followUpBody, recipient, sendAt, cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(order, nameof(order));

        // Call off any follow-up that has not yet gone out so a cancelled order never gets asked how its
        // delivery went.
        await CancelPendingFollowUpsAsync(order.Id, cancellationToken);

        var recipient = await ResolveRecipientAsync(order.BuyerId, cancellationToken);
        var body = $"eShopOnWeb: your order #{order.Id} has been cancelled.";
        await SendAndRecordAsync(order, NotificationKind.OrderCancelled, body, recipient, scheduleFor: null, cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(int orderId, bool refreshFromProvider, CancellationToken cancellationToken = default)
    {
        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByOrderSpecification(orderId), cancellationToken);

        if (refreshFromProvider)
        {
            foreach (var notification in notifications)
            {
                await RefreshFromProviderAsync(notification, cancellationToken);
            }
        }

        return notifications;
    }

    public async Task<Result<OrderNotification>> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Result<OrderNotification>.Invalid(new List<ValidationError> { new() { ErrorMessage = "An idempotency key is required." } });
        }

        // Repeating a request under the same key must not send a second message.
        var alreadyProduced = await _notificationRepository.FirstOrDefaultAsync(
            new OrderNotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (alreadyProduced is not null)
        {
            _logger.LogInformation("Resend idempotency key already produced notification {NotificationId}; not sending again.", alreadyProduced.Id);
            return Result<OrderNotification>.Success(alreadyProduced);
        }

        var original = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (original is null)
        {
            return Result<OrderNotification>.NotFound();
        }

        if (string.IsNullOrEmpty(original.ToPhoneNumber))
        {
            return Result<OrderNotification>.Invalid(new List<ValidationError>
            {
                new() { ErrorMessage = "The original notification has no recipient on file, so there is nothing to resend." }
            });
        }

        if (original.ContentRedacted || string.IsNullOrEmpty(original.Body))
        {
            return Result<OrderNotification>.Invalid(new List<ValidationError>
            {
                new() { ErrorMessage = "The original message's content has been disposed of and cannot be resent." }
            });
        }

        var resend = OrderNotification.ForSending(original.OrderId, original.OwnerId, original.Kind, original.ToPhoneNumber, original.Body);
        resend.MarkAsResendOf(original.Id, idempotencyKey);

        // A resend always goes out immediately, even if the original had been scheduled.
        await TrySendAsync(resend, new SmsSendRequest(original.ToPhoneNumber, original.Body), cancellationToken);
        resend = await _notificationRepository.AddAsync(resend, cancellationToken);

        _logger.LogInformation("Resent notification {OriginalId} as {NotificationId} for order {OrderId}.", original.Id, resend.Id, resend.OrderId);
        return Result<OrderNotification>.Success(resend);
    }

    public async Task<Result> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            return Result.NotFound();
        }

        if (notification.ContentRedacted)
        {
            return Result.Success();
        }

        // Dispose of the content at the provider first, so it is truly no longer retrievable there — not
        // merely hidden by this application. Only then clear the local copy.
        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            try
            {
                await _smsSender.RedactContentAsync(notification.ProviderMessageSid, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not dispose of content at the provider for notification {NotificationId} (error {Error}).",
                    notificationId, Scrub(ex.Message));
                return Result.Error("The message content could not be disposed of at the provider. Please try again.");
            }
        }

        notification.RedactContent();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
        _logger.LogInformation("Disposed of content for notification {NotificationId}.", notificationId);
        return Result.Success();
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var fromNumber = _smsSender.SendingNumber;

        // Ask the provider only for this application's own sending number's messages.
        var providerMessages = await _smsSender.ListAsync(new SmsListFilter
        {
            From = fromNumber,
            DateSentAfter = from,
            DateSentBefore = to
        }, cancellationToken);

        var eShopNotifications = await _notificationRepository.ListAsync(new SentOrderNotificationsInRangeSpecification(from, to), cancellationToken);

        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid))
            .GroupBy(m => m.Sid!)
            .ToDictionary(g => g.Key, g => g.First());

        var eShopBySid = eShopNotifications
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();
        var eShopOnly = new List<ReconciliationEntry>();

        foreach (var (sid, providerMessage) in providerBySid)
        {
            if (eShopBySid.TryGetValue(sid, out var notification))
            {
                matched.Add(BuildEntry(providerMessage, notification));
            }
            else
            {
                providerOnly.Add(BuildEntry(providerMessage, null));
            }
        }

        foreach (var (sid, notification) in eShopBySid)
        {
            if (!providerBySid.ContainsKey(sid))
            {
                eShopOnly.Add(BuildEntry(null, notification));
            }
        }

        return new ReconciliationReport
        {
            From = from,
            To = to,
            FromNumber = fromNumber,
            ProviderMessageCount = providerBySid.Count,
            EShopMessageCount = eShopBySid.Count,
            MatchedCount = matched.Count,
            ProviderOnlyCount = providerOnly.Count,
            EShopOnlyCount = eShopOnly.Count,
            Matched = matched,
            ProviderOnly = providerOnly,
            EShopOnly = eShopOnly
        };
    }

    // ----- helpers -----

    private async Task<string?> ResolveRecipientAsync(string ownerId, CancellationToken cancellationToken)
    {
        // Send to the shopper's most recently registered number (the spec orders newest first).
        var numbers = await _contactNumberRepository.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), cancellationToken);
        return numbers.FirstOrDefault()?.PhoneNumber;
    }

    private async Task SendAndRecordAsync(Order order, NotificationKind kind, string body, string? recipient, DateTimeOffset? scheduleFor, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(recipient))
        {
            // A shopper with no number on file is simply not messaged; the attempt is still recorded.
            var skipped = OrderNotification.NotAttempted(order.Id, order.BuyerId, kind);
            await _notificationRepository.AddAsync(skipped, cancellationToken);
            _logger.LogInformation("No contact number on file for order {OrderId}; {Kind} notification not sent.", order.Id, kind);
            return;
        }

        var notification = OrderNotification.ForSending(order.Id, order.BuyerId, kind, recipient, body);
        await TrySendAsync(notification, new SmsSendRequest(recipient, body, scheduleFor), cancellationToken);
        await _notificationRepository.AddAsync(notification, cancellationToken);
    }

    /// <summary>Attempts a send and records the outcome on the notification. Never throws.</summary>
    private async Task TrySendAsync(OrderNotification notification, SmsSendRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var message = await _smsSender.SendAsync(request, cancellationToken);
            notification.MarkSent(message.Sid, message.Status, message.ErrorCode, Scrub(message.ErrorMessage), request.ScheduleFor);
            _logger.LogInformation("Sent {Kind} for order {OrderId} as message {Sid} (status {Status}).",
                notification.Kind, notification.OrderId, message.Sid, message.Status);
        }
        catch (Exception ex)
        {
            // A message that cannot be sent must never fail the underlying operation.
            notification.MarkSendFailed(Scrub(ex.Message));
            _logger.LogWarning("Could not send {Kind} for order {OrderId} (error {Error}).",
                notification.Kind, notification.OrderId, Scrub(ex.Message));
        }
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByOrderSpecification(orderId), cancellationToken);
        var pendingFollowUps = notifications.Where(n =>
            n.Kind == NotificationKind.DeliveryFollowUp &&
            n.State == NotificationDeliveryState.Sent &&
            !string.IsNullOrEmpty(n.ProviderMessageSid) &&
            IsStillScheduled(n));

        foreach (var followUp in pendingFollowUps)
        {
            try
            {
                await _smsSender.CancelScheduledAsync(followUp.ProviderMessageSid!, cancellationToken);
                followUp.MarkCancelled();
                await _notificationRepository.UpdateAsync(followUp, cancellationToken);
                _logger.LogInformation("Cancelled scheduled follow-up {NotificationId} for order {OrderId}.", followUp.Id, orderId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not cancel scheduled follow-up {NotificationId} for order {OrderId} (error {Error}).",
                    followUp.Id, orderId, Scrub(ex.Message));
            }
        }
    }

    private static bool IsStillScheduled(OrderNotification notification)
    {
        if (string.Equals(notification.ProviderStatus, "scheduled", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Fall back to the scheduled time if the provider status has not been refreshed yet.
        return notification.ScheduledFor.HasValue && notification.ScheduledFor.Value > DateTimeOffset.UtcNow;
    }

    private async Task RefreshFromProviderAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            return;
        }

        if (notification.ProviderStatus is not null && TerminalStatuses.Contains(notification.ProviderStatus))
        {
            return;
        }

        try
        {
            var message = await _smsSender.GetAsync(notification.ProviderMessageSid, cancellationToken);
            notification.UpdateDeliveryStatus(message.Status, message.ErrorCode, Scrub(message.ErrorMessage));
            await _notificationRepository.UpdateAsync(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not refresh delivery status for notification {NotificationId} (error {Error}).",
                notification.Id, Scrub(ex.Message));
        }
    }

    private static ReconciliationEntry BuildEntry(SmsMessage? providerMessage, OrderNotification? notification)
    {
        return new ReconciliationEntry
        {
            ProviderMessageSid = providerMessage?.Sid ?? notification?.ProviderMessageSid,
            ProviderStatus = providerMessage?.Status ?? notification?.ProviderStatus,
            MaskedTo = MaskNumber(providerMessage?.To ?? notification?.ToPhoneNumber),
            DateSent = providerMessage?.DateSent,
            NotificationId = notification?.Id,
            OrderId = notification?.OrderId,
            Kind = notification?.Kind
        };
    }

    /// <summary>Masks a phone number to its last four digits so it can appear in a report without exposing it.</summary>
    private static string? MaskNumber(string? number)
    {
        if (string.IsNullOrEmpty(number))
        {
            return null;
        }

        var digits = new string(number.Where(char.IsDigit).ToArray());
        if (digits.Length <= 4)
        {
            return "••••";
        }

        return "••••••" + digits[^4..];
    }

    /// <summary>Redacts any phone-number-like run from provider-sourced text before it is stored or surfaced.</summary>
    private static string? Scrub(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        return PhoneLikePattern.Replace(text, "[redacted]");
    }
}
