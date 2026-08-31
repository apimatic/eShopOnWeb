using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Messaging;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    // Provider wire values (Twilio message statuses) the service reasons about.
    private const string ScheduledProviderStatus = "scheduled";
    private static readonly HashSet<string> TerminalProviderStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "delivered", "undelivered", "failed", "canceled"
    };

    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IMessagingProvider _messagingProvider;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<ContactNumber> contactNumberRepository,
        IRepository<OrderNotification> notificationRepository,
        IMessagingProvider messagingProvider,
        IOptions<TwilioSettings> settings,
        IAppLogger<OrderNotificationService> logger)
    {
        _contactNumberRepository = contactNumberRepository;
        _notificationRepository = notificationRepository;
        _messagingProvider = messagingProvider;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<ContactNumber> RegisterContactNumberAsync(string buyerId, string phoneNumber, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(phoneNumber, nameof(phoneNumber));

        VerifiedPhoneNumber? verified = await _messagingProvider.VerifyPhoneNumberAsync(phoneNumber, ct);
        if (verified is null || string.IsNullOrWhiteSpace(verified.CanonicalNumber))
        {
            throw new PhoneNumberNotValidException("The phone number is not a usable destination.");
        }

        var existing = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), ct);
        if (existing.Any(c => c.PhoneNumber == verified.CanonicalNumber))
        {
            throw new DuplicateException("This phone number is already registered.");
        }

        var contactNumber = new ContactNumber(buyerId, verified.CanonicalNumber);
        return await _contactNumberRepository.AddAsync(contactNumber, ct);
    }

    public async Task<IReadOnlyList<ContactNumber>> GetContactNumbersAsync(string buyerId, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), ct);
    }

    public async Task<bool> DeleteContactNumberAsync(string buyerId, int contactNumberId, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var contactNumber = await _contactNumberRepository.GetByIdAsync(contactNumberId, ct);
        if (contactNumber is null || contactNumber.BuyerId != buyerId)
        {
            return false;
        }

        await _contactNumberRepository.DeleteAsync(contactNumber, ct);
        return true;
    }

    public Task NotifyOrderPlacedAsync(Order order, CancellationToken ct)
    {
        Guard.Against.Null(order, nameof(order));
        var body = string.Format(
            CultureInfo.InvariantCulture,
            "eShop: Your order #{0} has been placed. Total: USD {1:0.00}. Thank you for shopping with us!",
            order.Id, order.Total());
        return NotifyAllContactNumbersAsync(order, NotificationKind.OrderPlaced, body, scheduledFor: null, ct);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken ct)
    {
        Guard.Against.Null(order, nameof(order));

        var body = $"eShop: Good news! Your order #{order.Id} is on its way.";
        await NotifyAllContactNumbersAsync(order, NotificationKind.OrderDispatched, body, scheduledFor: null, ct);

        var sendAt = DateTimeOffset.UtcNow.AddDays(_settings.FollowUpDelayDays);
        var followUpBody = $"eShop: Your order #{order.Id} should have arrived by now. How did the delivery go?";
        await NotifyAllContactNumbersAsync(order, NotificationKind.DeliveryFollowUp, followUpBody, sendAt, ct);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken ct)
    {
        Guard.Against.Null(order, nameof(order));

        // Call off any follow-up that has not gone out yet, before telling the shopper.
        var pendingFollowUps = await _notificationRepository.ListAsync(
            new PendingFollowUpsByOrderSpecification(order.Id, ScheduledProviderStatus), ct);
        foreach (var followUp in pendingFollowUps)
        {
            await CancelFollowUpWithRetryAsync(followUp, order.Id, ct);
        }

        var body = $"eShop: Your order #{order.Id} has been cancelled. Please contact support if this is unexpected.";
        await NotifyAllContactNumbersAsync(order, NotificationKind.OrderCancelled, body, scheduledFor: null, ct);
    }

    public async Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(int orderId, bool syncWithProvider, CancellationToken ct)
    {
        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByOrderSpecification(orderId), ct);

        if (syncWithProvider)
        {
            foreach (var notification in notifications)
            {
                if (notification.ProviderMessageSid is null || TerminalProviderStatuses.Contains(notification.ProviderStatus))
                {
                    continue;
                }

                try
                {
                    var current = await _messagingProvider.FetchMessageAsync(notification.ProviderMessageSid, ct);
                    notification.UpdateProviderState(current.Sid, current.Status, current.ErrorCode, current.ErrorMessage);
                    await _notificationRepository.UpdateAsync(notification, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        "Failed to refresh notification {NotificationId} from the provider: {Error}",
                        notification.Id, ex.Message);
                }
            }
        }

        return notifications;
    }

    public async Task<ResendNotificationResult> ResendNotificationAsync(int notificationId, string idempotencyKey, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var original = await _notificationRepository.GetByIdAsync(notificationId, ct);
        Guard.Against.Null(original, nameof(notificationId));

        var existing = await _notificationRepository.FirstOrDefaultAsync(
            new OrderNotificationByIdempotencyKeySpecification(idempotencyKey), ct);
        if (existing is not null)
        {
            return new ResendNotificationResult(existing, IdempotentReplay: true);
        }

        if (original.BodyRedacted || original.Body is null)
        {
            throw new DomainRuleViolationException("The content of this message has been disposed of and can no longer be sent.");
        }

        var resend = new OrderNotification(
            original.OrderId, original.BuyerId, original.ToNumber, NotificationKind.Resend,
            original.Body, scheduledFor: null, resendOfId: original.Id, idempotencyKey: idempotencyKey);
        resend = await _notificationRepository.AddAsync(resend, ct);

        try
        {
            var sent = await _messagingProvider.SendMessageAsync(resend.ToNumber, resend.Body, ct);
            resend.UpdateProviderState(sent.Sid, sent.Status, sent.ErrorCode, sent.ErrorMessage);
        }
        catch (MessagingProviderException)
        {
            resend.UpdateProviderState(null, "failed", null, null);
            await _notificationRepository.UpdateAsync(resend, ct);
            throw;
        }

        await _notificationRepository.UpdateAsync(resend, ct);
        return new ResendNotificationResult(resend, IdempotentReplay: false);
    }

    public async Task RedactNotificationContentAsync(int notificationId, CancellationToken ct)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, ct);
        Guard.Against.Null(notification, nameof(notificationId));

        if (notification.BodyRedacted)
        {
            return;
        }

        if (notification.ProviderMessageSid is not null)
        {
            await _messagingProvider.RedactMessageBodyAsync(notification.ProviderMessageSid, ct);
        }

        notification.MarkBodyRedacted();
        await _notificationRepository.UpdateAsync(notification, ct);
    }

    public async Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        if (to < from)
        {
            throw new DomainRuleViolationException("The 'to' boundary must not be earlier than the 'from' boundary.");
        }

        var providerMessages = await _messagingProvider.ListSentMessagesAsync(from, to, ct);
        var localNotifications = await _notificationRepository.ListAsync(
            new OrderNotificationsCreatedBetweenSpecification(from, to), ct);

        var localBySid = localNotifications
            .Where(n => n.ProviderMessageSid is not null)
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var entries = new List<ReconciliationEntry>();
        var matchedLocalIds = new HashSet<int>();

        foreach (var message in providerMessages)
        {
            if (localBySid.TryGetValue(message.Sid, out var local))
            {
                matchedLocalIds.Add(local.Id);
                entries.Add(new ReconciliationEntry(
                    message.Sid, local.Id, local.OrderId, message.To,
                    message.Status, local.ProviderStatus, message.DateSent,
                    ReconciliationDisposition.Matched));
            }
            else
            {
                entries.Add(new ReconciliationEntry(
                    message.Sid, null, null, message.To,
                    message.Status, null, message.DateSent,
                    ReconciliationDisposition.MissingLocally));
            }
        }

        // The provider's list is anchored on date-sent, so messages it never sent
        // (scheduled-then-cancelled follow-ups) legitimately do not appear in it.
        // Flagging those as missing would be noise, not a discrepancy.
        var neverSent = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ScheduledProviderStatus, "canceled" };

        foreach (var local in localNotifications.Where(n => n.ProviderMessageSid is not null && !matchedLocalIds.Contains(n.Id)))
        {
            if (neverSent.Contains(local.ProviderStatus))
            {
                continue;
            }

            entries.Add(new ReconciliationEntry(
                local.ProviderMessageSid, local.Id, local.OrderId, null,
                null, local.ProviderStatus, null,
                ReconciliationDisposition.MissingAtProvider));
        }

        return new NotificationReconciliationReport(
            from, to, providerMessages.Count, localNotifications.Count, entries);
    }

    // A just-created scheduled message is not immediately addressable at the provider:
    // an update within the first few seconds after creation fails with HTTP 404 even
    // though the create returned the message's Sid. Retry through that window, but only
    // while the message is young — a 404 long after creation means the resource is gone.
    private static readonly TimeSpan[] CancelRetryDelays =
    {
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(20)
    };
    private static readonly TimeSpan CancelPropagationWindow = TimeSpan.FromMinutes(2);

    private async Task CancelFollowUpWithRetryAsync(OrderNotification followUp, int orderId, CancellationToken ct)
    {
        var recentlyCreated = DateTimeOffset.UtcNow - followUp.CreatedAt < CancelPropagationWindow;

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var cancelled = await _messagingProvider.CancelScheduledMessageAsync(followUp.ProviderMessageSid!, ct);
                followUp.UpdateProviderState(cancelled.Sid, cancelled.Status, cancelled.ErrorCode, cancelled.ErrorMessage);
                await _notificationRepository.UpdateAsync(followUp, ct);
                return;
            }
            catch (MessagingProviderException ex)
                when (ex.StatusCode == HttpStatusCode.NotFound && recentlyCreated && attempt < CancelRetryDelays.Length)
            {
                _logger.LogInformation(
                    "Scheduled follow-up notification {NotificationId} for order {OrderId} is not yet addressable at the provider; retrying cancel in {DelaySeconds}s.",
                    followUp.Id, orderId, CancelRetryDelays[attempt].TotalSeconds);
                await Task.Delay(CancelRetryDelays[attempt], ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Failed to cancel scheduled follow-up notification {NotificationId} for order {OrderId} at the provider: {Error}. The message may still be delivered at its scheduled time.",
                    followUp.Id, orderId, ex.Message);
                return;
            }
        }
    }

    private async Task NotifyAllContactNumbersAsync(Order order, NotificationKind kind, string body, DateTimeOffset? scheduledFor, CancellationToken ct)
    {
        var contactNumbers = await _contactNumberRepository.ListAsync(
            new ContactNumbersByBuyerSpecification(order.BuyerId), ct);

        foreach (var contactNumber in contactNumbers)
        {
            var notification = new OrderNotification(order.Id, order.BuyerId, contactNumber.PhoneNumber, kind, body, scheduledFor);
            notification = await _notificationRepository.AddAsync(notification, ct);

            try
            {
                var sent = scheduledFor.HasValue
                    ? await _messagingProvider.ScheduleMessageAsync(contactNumber.PhoneNumber, body, scheduledFor.Value, ct)
                    : await _messagingProvider.SendMessageAsync(contactNumber.PhoneNumber, body, ct);

                notification.UpdateProviderState(sent.Sid, sent.Status, sent.ErrorCode, sent.ErrorMessage);
            }
            catch (Exception ex)
            {
                // A message that cannot be sent must never fail the underlying order operation.
                notification.UpdateProviderState(null, "failed", null, null);
                _logger.LogWarning(
                    "Failed to send {Kind} notification {NotificationId} for order {OrderId}: {Error}",
                    kind, notification.Id, order.Id, ex.Message);
            }

            await _notificationRepository.UpdateAsync(notification, ct);
        }
    }
}
