using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly ISmsProvider _smsProvider;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        ISmsProvider smsProvider,
        IAppLogger<ContactNumberService> logger)
    {
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _smsProvider = smsProvider;
        _logger = logger;
    }

    public async Task<ContactNumberRegistrationResult> RegisterAsync(string ownerId, string rawNumber, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));

        if (string.IsNullOrWhiteSpace(rawNumber))
        {
            return ContactNumberRegistrationResult.Rejected("A phone number is required.");
        }

        // Reject an unusable destination here, at registration, rather than at send time.
        // The provider's lookup also yields the canonical E.164 form we persist.
        var lookup = await _smsProvider.LookupAsync(rawNumber, cancellationToken);
        if (!lookup.IsValid || string.IsNullOrEmpty(lookup.CanonicalNumber))
        {
            _logger.LogWarning("Rejected a contact number registration for owner {OwnerId}: provider considers it not a usable destination.", ownerId);
            return ContactNumberRegistrationResult.Rejected("The number is not a usable messaging destination.");
        }

        var canonical = lookup.CanonicalNumber;

        // Idempotent on the canonical number: if the shopper already has it, return that.
        var existing = await _contactNumbers.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), cancellationToken);
        var already = existing.FirstOrDefault(c => c.PhoneNumber == canonical);
        if (already is not null)
        {
            return ContactNumberRegistrationResult.Ok(already);
        }

        var contactNumber = new ContactNumber(ownerId, canonical);
        contactNumber = await _contactNumbers.AddAsync(contactNumber, cancellationToken);
        _logger.LogInformation("Registered a contact number for owner {OwnerId} (id {ContactNumberId}).", ownerId, contactNumber.Id);
        return ContactNumberRegistrationResult.Ok(contactNumber);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), cancellationToken);
        return numbers;
    }

    public async Task<bool> RemoveAsync(string ownerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));

        // Owner-scoped: a shopper can only remove their own number.
        var contactNumber = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByIdForOwnerSpecification(contactNumberId, ownerId), cancellationToken);
        if (contactNumber is null)
        {
            return false;
        }

        // Nothing may be sent to this number again: call off any still-scheduled
        // follow-ups queued with the provider for it.
        var pending = await _notifications.ListAsync(
            new PendingScheduledNotificationsByNumberSpecification(contactNumber.PhoneNumber), cancellationToken);
        foreach (var notification in pending)
        {
            try
            {
                await _smsProvider.CancelScheduledAsync(notification.ProviderMessageSid!, cancellationToken);
                notification.MarkCanceled();
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (System.Exception ex)
            {
                // Best-effort: do not fail the removal because a cancel call hiccupped.
                _logger.LogWarning("Failed to cancel a scheduled follow-up while removing a contact number for owner {OwnerId}: {Error}", ownerId, ex.Message);
            }
        }

        await _contactNumbers.DeleteAsync(contactNumber, cancellationToken);
        _logger.LogInformation("Removed contact number {ContactNumberId} for owner {OwnerId}.", contactNumberId, ownerId);
        return true;
    }
}
