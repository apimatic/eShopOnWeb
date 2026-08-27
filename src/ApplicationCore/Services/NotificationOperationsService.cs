using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class NotificationOperationsService : INotificationOperationsService
{
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly ISmsService _smsService;
    private readonly IAppLogger<NotificationOperationsService> _logger;

    public NotificationOperationsService(IRepository<OrderNotification> notificationRepository,
        IRepository<ContactNumber> contactNumberRepository,
        ISmsService smsService,
        IAppLogger<NotificationOperationsService> logger)
    {
        _notificationRepository = notificationRepository;
        _contactNumberRepository = contactNumberRepository;
        _smsService = smsService;
        _logger = logger;
    }

    public async Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        // Repeating a request under the same key must not send a second message.
        var existing = await _notificationRepository.FirstOrDefaultAsync(
            new OrderNotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (existing != null)
        {
            return new ResendResult { Outcome = ResendOutcome.Sent, Notification = existing, IsIdempotentReplay = true };
        }

        var original = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (original == null)
        {
            return new ResendResult { Outcome = ResendOutcome.NotificationNotFound, Error = "Notification not found." };
        }

        if (original.Body == null)
        {
            return new ResendResult { Outcome = ResendOutcome.NothingToResend, Error = "The content of this message has been disposed of and cannot be re-sent." };
        }

        // Nothing may be sent to a number the shopper has removed.
        var contactNumber = await _contactNumberRepository.GetByIdAsync(original.ContactNumberId, cancellationToken);
        if (contactNumber == null || contactNumber.BuyerId != original.BuyerId)
        {
            return new ResendResult { Outcome = ResendOutcome.DestinationNoLongerRegistered, Error = "The destination number is no longer registered to this shopper." };
        }

        var resend = new OrderNotification(original.OrderId, original.BuyerId, original.ContactNumberId,
            contactNumber.PhoneNumber, original.NotificationType, original.Body,
            idempotencyKey: idempotencyKey, resendOfNotificationId: original.Id);

        try
        {
            var result = await _smsService.SendMessageAsync(contactNumber.PhoneNumber, original.Body, cancellationToken);
            if (result.Accepted && result.ProviderMessageSid != null)
            {
                resend.MarkProviderAccepted(result.ProviderMessageSid, result.Status ?? "queued");
            }
            else
            {
                resend.MarkFailed(result.ErrorMessage ?? "The messaging provider rejected the message.", result.ErrorCode);
            }
        }
        catch (Exception ex)
        {
            resend.MarkFailed("The messaging provider could not be reached.");
            _logger.LogWarning("Failed to resend notification {NotificationId}: {ExceptionType}", notificationId, ex.GetType().Name);
        }

        await _notificationRepository.AddAsync(resend, cancellationToken);
        return new ResendResult { Outcome = ResendOutcome.Sent, Notification = resend };
    }

    public async Task<ContentDisposalResult> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (notification == null)
        {
            return new ContentDisposalResult { NotificationNotFound = true, Error = "Notification not found." };
        }

        if (notification.ContentRedacted)
        {
            return new ContentDisposalResult { Succeeded = true };
        }

        if (notification.ProviderMessageSid != null)
        {
            // The provider can briefly refuse to update a message that was created moments
            // ago; retry with backoff before giving up.
            bool redacted = false;
            try
            {
                for (var attempt = 0; attempt < 4 && !redacted; attempt++)
                {
                    if (attempt > 0)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(2 * attempt), cancellationToken);
                    }
                    redacted = await _smsService.RedactMessageBodyAsync(notification.ProviderMessageSid, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to redact provider content for notification {NotificationId}: {ExceptionType}", notificationId, ex.GetType().Name);
                return new ContentDisposalResult { Error = "The messaging provider could not be reached; the content has not been disposed of." };
            }

            if (!redacted)
            {
                return new ContentDisposalResult { Error = "The messaging provider refused to redact the message content." };
            }
        }

        notification.MarkContentRedacted();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
        return new ContentDisposalResult { Succeeded = true };
    }
}
