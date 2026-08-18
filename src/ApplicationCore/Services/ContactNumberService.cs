using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IRepository<Notification> _notificationRepository;
    private readonly ISmsGateway _smsGateway;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ContactNumber> contactNumberRepository,
        IRepository<Notification> notificationRepository,
        ISmsGateway smsGateway,
        IAppLogger<ContactNumberService> logger)
    {
        _contactNumberRepository = contactNumberRepository;
        _notificationRepository = notificationRepository;
        _smsGateway = smsGateway;
        _logger = logger;
    }

    public async Task<ContactNumberRegistrationResult> RegisterAsync(string ownerId, string rawNumber, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(rawNumber, nameof(rawNumber));

        // Reject an unusable destination now, at registration — not when a later message fails to go out.
        var lookup = await _smsGateway.LookupNumberAsync(rawNumber, ct);
        if (!lookup.IsValid || string.IsNullOrEmpty(lookup.CanonicalE164))
        {
            // Deliberately do not log the number itself.
            _logger.LogWarning("Rejected contact-number registration for owner {OwnerId}: not a usable destination.", ownerId);
            return new ContactNumberRegistrationResult(
                ContactNumberRegistrationOutcome.Rejected, null, "The number is not a usable destination.");
        }

        // Store the provider's own canonical E.164 form, not whatever the caller typed.
        var contactNumber = new ContactNumber(ownerId, lookup.CanonicalE164);
        contactNumber = await _contactNumberRepository.AddAsync(contactNumber, ct);
        return new ContactNumberRegistrationResult(ContactNumberRegistrationOutcome.Registered, contactNumber, null);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string ownerId, CancellationToken ct)
    {
        return await _contactNumberRepository.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), ct);
    }

    public async Task<bool> RemoveAsync(string ownerId, int contactNumberId, CancellationToken ct)
    {
        var contactNumber = await _contactNumberRepository.FirstOrDefaultAsync(
            new ContactNumberByIdForOwnerSpecification(contactNumberId, ownerId), ct);
        if (contactNumber is null)
        {
            return false;
        }

        // Nothing may be sent to this number again: call off any follow-up the provider still holds for it.
        await CancelPendingScheduledMessagesAsync(contactNumber.E164Number, ct);

        await _contactNumberRepository.DeleteAsync(contactNumber, ct);
        return true;
    }

    private async Task CancelPendingScheduledMessagesAsync(string e164Number, CancellationToken ct)
    {
        var pending = await _notificationRepository.ListAsync(
            new PendingScheduledNotificationsByNumberSpecification(e164Number), ct);

        foreach (var notification in pending)
        {
            if (notification.ProviderMessageSid is null)
            {
                continue;
            }

            try
            {
                await _smsGateway.CancelScheduledAsync(notification.ProviderMessageSid, ct);
                notification.MarkCanceled();
                await _notificationRepository.UpdateAsync(notification, ct);
            }
            catch (SmsGatewayException ex)
            {
                // Best effort — a message already sent cannot be recalled. Never fail the removal over it.
                _logger.LogWarning(
                    "Could not cancel scheduled notification {NotificationId} on number removal: provider status {Status}.",
                    notification.Id, ex.StatusCode);
            }
        }
    }
}
