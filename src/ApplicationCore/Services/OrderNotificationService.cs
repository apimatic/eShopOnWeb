using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Extensions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    public static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<NotificationContentRedaction> _redactions;
    private readonly IRepository<ShopperContactNumber> _contactNumbers;
    private readonly ITwilioMessagingClient _messagingClient;
    private readonly ITwilioSettingsAccessor _twilioSettings;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<OrderNotification> notifications,
        IRepository<NotificationContentRedaction> redactions,
        IRepository<ShopperContactNumber> contactNumbers,
        ITwilioMessagingClient messagingClient,
        ITwilioSettingsAccessor twilioSettings,
        IAppLogger<OrderNotificationService> logger)
    {
        _notifications = notifications;
        _redactions = redactions;
        _contactNumbers = contactNumbers;
        _messagingClient = messagingClient;
        _twilioSettings = twilioSettings;
        _logger = logger;
    }

    public Task TryNotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        return TrySendAsync(
            order,
            NotificationKind.OrderPlaced,
            $"Your eShop order #{order.Id} has been placed. Thank you for your order.",
            sendAt: null,
            cancellationToken);
    }

    public async Task TryNotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        await TrySendAsync(
            order,
            NotificationKind.OrderDispatched,
            $"Your eShop order #{order.Id} is on its way.",
            sendAt: null,
            cancellationToken);

        await TrySendAsync(
            order,
            NotificationKind.DeliveryFollowUp,
            $"How did the delivery of your eShop order #{order.Id} go?",
            sendAt: DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay),
            cancellationToken);
    }

    public async Task TryNotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        await CancelPendingFollowUpsAsync(order.Id, cancellationToken);

        await TrySendAsync(
            order,
            NotificationKind.OrderCancelled,
            $"Your eShop order #{order.Id} has been cancelled.",
            sendAt: null,
            cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(int orderId, bool refreshFromProvider, CancellationToken cancellationToken = default)
    {
        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderIdSpecification(orderId), cancellationToken);
        if (refreshFromProvider)
        {
            foreach (var notification in notifications)
            {
                await RefreshFromProviderAsync(notification, cancellationToken);
            }
        }

        await ApplyStoredRedactionsAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var existingResend = await _notifications.FirstOrDefaultAsync(
            new ResendBySourceAndKeySpecification(notificationId, idempotencyKey),
            cancellationToken);
        if (existingResend != null)
        {
            return existingResend;
        }

        var original = await _notifications.FirstOrDefaultAsync(
            new OrderNotificationByIdSpecification(notificationId),
            cancellationToken);
        if (original == null)
        {
            throw new NotificationNotFoundException(notificationId);
        }

        if (original.ContentRedacted || string.IsNullOrEmpty(original.Body) || await IsRedactedAsync(original.Id, cancellationToken))
        {
            throw new NotificationCannotBeResentException("The original message content is no longer available to resend.");
        }

        var contactStillRegistered = original.ContactNumberId.HasValue
            && await _contactNumbers.FirstOrDefaultAsync(new ContactNumberByIdSpecification(original.ContactNumberId.Value), cancellationToken) != null;
        if (!contactStillRegistered)
        {
            throw new NotificationCannotBeResentException("The destination contact number is no longer on file and cannot be messaged.");
        }

        var sent = await SendToProviderAsync(original.DestinationNumber, original.Body, sendAt: null, cancellationToken);
        var notification = new OrderNotification(
            original.OrderId,
            original.BuyerId,
            original.ContactNumberId,
            original.DestinationNumber,
            original.Kind,
            original.Body,
            sent.Message?.Sid,
            sent.Message?.Status ?? (sent.Succeeded ? null : "failed"),
            sent.Message?.ErrorCode,
            sent.RedactedError,
            sendAt: null,
            sourceNotificationId: original.Id,
            resendIdempotencyKey: idempotencyKey);

        if (!sent.Succeeded && sent.Message == null)
        {
            notification.MarkSendFailed(sent.RedactedError ?? "The provider did not accept the message.");
        }

        return await _notifications.AddAsync(notification, cancellationToken);
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.FirstOrDefaultAsync(
            new OrderNotificationByIdSpecification(notificationId),
            cancellationToken);
        if (notification == null)
        {
            throw new NotificationNotFoundException(notificationId);
        }

        if (!await IsRedactedAsync(notificationId, cancellationToken))
        {
            await _redactions.AddAsync(new NotificationContentRedaction(notificationId), cancellationToken);
        }

        notification.RedactContent();
        await _notifications.SaveChangesAsync(cancellationToken);

        if (string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            return;
        }

        try
        {
            await _messagingClient.UpdateMessageAsync(
                notification.ProviderMessageSid,
                new UpdateProviderMessageRequest { Body = string.Empty },
                cancellationToken);
        }
        catch (Exception ex)
        {
            if (!IsRedactionAccepted(ex))
            {
                _logger.LogWarning(
                    "Failed to redact provider content for notification {NotificationId}: {Error}",
                    notification.Id,
                    PhoneNumberRedactor.Redact(ex.Message));
                throw;
            }

            _logger.LogInformation(
                "Provider accepted content disposal for notification {NotificationId} and will finish it once the message is finalized.",
                notification.Id);
        }

        try
        {
            var latest = await _messagingClient.FetchMessageAsync(notification.ProviderMessageSid, cancellationToken);
            notification.ApplyProviderState(latest.Sid, latest.Status, latest.ErrorCode, latest.ErrorMessage, string.Empty);
            notification.RedactContent();
            await _notifications.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Provider content was disposed but the follow-up fetch did not succeed for notification {NotificationId}: {Error}",
                notification.Id,
                PhoneNumberRedactor.Redact(ex.Message));
        }
    }

    public async Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new ArgumentException("The reconciliation range is invalid.", nameof(to));
        }

        var fromNumber = _twilioSettings.FromNumber;
        var providerMessages = await _messagingClient.ListMessagesAsync(
            new ListProviderMessagesRequest
            {
                From = fromNumber,
                DateSentAfter = from,
                DateSentBefore = to
            },
            cancellationToken);

        var local = await _notifications.ListAsync(new OrderNotificationsInRangeSpecification(from, to), cancellationToken);
        var localBySid = local
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var report = new NotificationReconciliationReport
        {
            From = from,
            To = to,
            FromNumber = fromNumber
        };

        var matchedSids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var provider in providerMessages)
        {
            if (string.IsNullOrEmpty(provider.Sid))
            {
                continue;
            }

            if (localBySid.TryGetValue(provider.Sid, out var localNotification))
            {
                matchedSids.Add(provider.Sid);
                report.Matched.Add(new ReconciledMessage
                {
                    NotificationId = localNotification.Id,
                    ProviderMessageSid = provider.Sid,
                    LocalStatus = localNotification.ProviderStatus,
                    ProviderStatus = provider.Status
                });
            }
            else
            {
                report.ProviderOnly.Add(new ProviderOnlyMessage
                {
                    ProviderMessageSid = provider.Sid,
                    Status = provider.Status,
                    DateCreated = provider.DateCreated,
                    DateSent = provider.DateSent
                });
            }
        }

        foreach (var notification in local)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid) || !matchedSids.Contains(notification.ProviderMessageSid))
            {
                report.LocalOnly.Add(new LocalOnlyNotification
                {
                    NotificationId = notification.Id,
                    ProviderMessageSid = notification.ProviderMessageSid,
                    Status = notification.ProviderStatus,
                    CreatedAt = notification.CreatedAt
                });
            }
        }

        return report;
    }

    private async Task TrySendAsync(
        Order order,
        NotificationKind kind,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var contact = await GetActiveContactAsync(order.BuyerId, cancellationToken);
            if (contact == null)
            {
                return;
            }

            var sent = await SendToProviderAsync(contact.CanonicalNumber, body, sendAt, cancellationToken);
            var notification = new OrderNotification(
                order.Id,
                order.BuyerId,
                contact.Id,
                contact.CanonicalNumber,
                kind,
                body,
                sent.Message?.Sid,
                sent.Message?.Status ?? (sent.Succeeded ? null : "failed"),
                sent.Message?.ErrorCode,
                sent.RedactedError,
                sendAt);

            if (!sent.Succeeded && sent.Message == null)
            {
                notification.MarkSendFailed(sent.RedactedError ?? "The provider did not accept the message.");
            }

            await _notifications.AddAsync(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Failed to send {Kind} notification for order {OrderId}: {Error}",
                kind,
                order.Id,
                PhoneNumberRedactor.Redact(ex.Message));
        }
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var pending = await _notifications.ListAsync(new PendingFollowUpNotificationsSpecification(orderId), cancellationToken);
        foreach (var followUp in pending)
        {
            try
            {
                if (string.IsNullOrEmpty(followUp.ProviderMessageSid))
                {
                    continue;
                }

                await RefreshFromProviderAsync(followUp, cancellationToken);
                if (!followUp.IsPendingSend())
                {
                    continue;
                }

                var updated = await _messagingClient.UpdateMessageAsync(
                    followUp.ProviderMessageSid,
                    new UpdateProviderMessageRequest { Status = "canceled" },
                    cancellationToken);
                followUp.ApplyProviderState(updated.Sid, updated.Status, updated.ErrorCode, updated.ErrorMessage, updated.Body);
                await _notifications.UpdateAsync(followUp, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Failed to cancel follow-up notification {NotificationId} for order {OrderId}: {Error}",
                    followUp.Id,
                    orderId,
                    PhoneNumberRedactor.Redact(ex.Message));
            }
        }
    }

    private async Task RefreshFromProviderAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            return;
        }

        try
        {
            var latest = await _messagingClient.FetchMessageAsync(notification.ProviderMessageSid, cancellationToken);
            var redacted = notification.ContentRedacted || await IsRedactedAsync(notification.Id, cancellationToken);
            var body = redacted ? string.Empty : latest.Body;
            notification.ApplyProviderState(latest.Sid, latest.Status, latest.ErrorCode, latest.ErrorMessage, body);
            if (redacted)
            {
                notification.RedactContent();
            }

            await _notifications.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Failed to refresh notification {NotificationId} from the provider: {Error}",
                notification.Id,
                PhoneNumberRedactor.Redact(ex.Message));
        }
    }

    private async Task ApplyStoredRedactionsAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        if (notifications.Count == 0)
        {
            return;
        }

        var ids = notifications.Select(n => n.Id).ToArray();
        var redactions = await _redactions.ListAsync(new NotificationContentRedactionByNotificationIdSpecification(ids), cancellationToken);
        var redactedIds = redactions.Select(r => r.NotificationId).ToHashSet();
        foreach (var notification in notifications)
        {
            if (redactedIds.Contains(notification.Id))
            {
                notification.RedactContent();
            }
        }
    }

    private async Task<bool> IsRedactedAsync(int notificationId, CancellationToken cancellationToken)
    {
        var existing = await _redactions.FirstOrDefaultAsync(
            new NotificationContentRedactionByNotificationIdSpecification(notificationId),
            cancellationToken);
        return existing != null;
    }

    private static bool IsRedactionAccepted(Exception ex)
    {
        var text = ex.Message ?? string.Empty;
        return text.Contains("20409", StringComparison.Ordinal)
               || text.Contains("already in queue", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<ShopperContactNumber?> GetActiveContactAsync(string buyerId, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return numbers.FirstOrDefault();
    }

    private async Task<SendAttempt> SendToProviderAsync(
        string destination,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var request = new CreateProviderMessageRequest
            {
                To = destination,
                Body = body,
                From = _twilioSettings.FromNumber,
                MessagingServiceSid = sendAt.HasValue ? _twilioSettings.MessagingServiceSid : _twilioSettings.MessagingServiceSid,
                ScheduleType = sendAt.HasValue ? "fixed" : null,
                SendAt = sendAt
            };

            var message = await _messagingClient.CreateMessageAsync(request, cancellationToken);
            return new SendAttempt(true, message, PhoneNumberRedactor.Redact(message.ErrorMessage));
        }
        catch (Exception ex)
        {
            var redacted = PhoneNumberRedactor.Redact(ex.Message);
            _logger.LogWarning("Provider rejected or failed a message send: {Error}", redacted);
            return new SendAttempt(false, null, redacted);
        }
    }

    private readonly record struct SendAttempt(bool Succeeded, ProviderMessage? Message, string? RedactedError);
}
