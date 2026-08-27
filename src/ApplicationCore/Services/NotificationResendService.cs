using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class NotificationResendService : INotificationResendService
{
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly ITextMessagingService _messagingService;
    private readonly IAppLogger<NotificationResendService> _logger;

    public NotificationResendService(
        IRepository<OrderNotification> notificationRepository,
        IRepository<ContactNumber> contactNumberRepository,
        ITextMessagingService messagingService,
        IAppLogger<NotificationResendService> logger)
    {
        _notificationRepository = notificationRepository;
        _contactNumberRepository = contactNumberRepository;
        _messagingService = messagingService;
        _logger = logger;
    }

    public async Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct = default)
    {
        // A repeat under the same key returns the message the first request produced.
        var alreadyProcessed = await _notificationRepository.FirstOrDefaultAsync(
            new OrderNotificationByIdempotencyKeySpecification(idempotencyKey), ct);
        if (alreadyProcessed is not null)
        {
            return new ResendResult(ResendOutcome.AlreadyProcessed, alreadyProcessed);
        }

        var original = await _notificationRepository.GetByIdAsync(notificationId, ct);
        if (original is null)
        {
            return new ResendResult(ResendOutcome.NotFound, null);
        }

        if (original.ContentDisposed || original.Body is null)
        {
            return new ResendResult(ResendOutcome.ContentDisposed, original);
        }

        // Settle what became of the original before spending another message.
        if (original.MessageSid is not null)
        {
            try
            {
                var current = await _messagingService.GetMessageAsync(original.MessageSid, ct);
                original.UpdateDeliveryState(current.Status, current.ErrorCode, current.ErrorMessage);
                await _notificationRepository.UpdateAsync(original, ct);

                if (string.Equals(current.Status, "delivered", StringComparison.OrdinalIgnoreCase))
                {
                    return new ResendResult(ResendOutcome.AlreadyDelivered, original);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The provider's state is unreadable right now; the operator's explicit request stands.
                _logger.LogWarning($"Could not refresh notification {original.Id} before resend: {ex.Message}");
            }
        }

        var contactNumber = await _contactNumberRepository.GetByIdAsync(original.ContactNumberId, ct);
        if (contactNumber is null)
        {
            return new ResendResult(ResendOutcome.ContactNumberRemoved, original);
        }

        var resend = new OrderNotification(original.OrderId, original.BuyerId, original.ContactNumberId, original.Kind, original.Body, idempotencyKey);
        resend = await _notificationRepository.AddAsync(resend, ct);

        try
        {
            var result = await _messagingService.SendMessageAsync(contactNumber.PhoneNumber, original.Body, ct);
            resend.MarkAccepted(result.Sid, result.Status);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning($"Resend of notification {original.Id} failed: {ex.Message}");
            resend.MarkSendFailed(ex.Message);
        }

        await _notificationRepository.UpdateAsync(resend, ct);
        return new ResendResult(ResendOutcome.Sent, resend);
    }
}
