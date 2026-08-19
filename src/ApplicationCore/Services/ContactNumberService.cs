using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Registers, lists and removes a shopper's contact numbers. Registration rejects any number the
/// provider does not consider a usable destination, and stores the provider's canonical form.
/// </summary>
public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly ISmsGateway _gateway;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ContactNumber> contactNumbers,
        ISmsGateway gateway,
        IAppLogger<ContactNumberService> logger)
    {
        _contactNumbers = contactNumbers;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<ContactNumberView> RegisterAsync(string ownerId, string rawNumber, CancellationToken cancellationToken = default)
    {
        // Reject an unusable destination here, at registration, rather than when a later message fails.
        var lookup = await _gateway.LookupAsync(rawNumber, cancellationToken);
        if (!lookup.IsValid || string.IsNullOrWhiteSpace(lookup.CanonicalE164))
        {
            // The number itself is PII, so it is not included in the message.
            throw new InvalidPhoneNumberException("The phone number is not a valid, reachable destination and cannot be registered.");
        }

        var canonical = lookup.CanonicalE164!;

        // If the owner already has this exact number on file, return it rather than duplicating.
        var owned = await _contactNumbers.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), cancellationToken);
        var existing = owned.FirstOrDefault(c => c.E164Number == canonical);
        if (existing is not null)
        {
            return ToView(existing);
        }

        var contactNumber = new ContactNumber(ownerId, canonical);
        await _contactNumbers.AddAsync(contactNumber, cancellationToken);
        _logger.LogInformation($"Registered a contact number (id {contactNumber.Id}) for owner {ownerId}.");
        return ToView(contactNumber);
    }

    public async Task<IReadOnlyList<ContactNumberView>> ListAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        var owned = await _contactNumbers.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), cancellationToken);
        return owned.Select(ToView).ToList();
    }

    public async Task<bool> DeleteAsync(string ownerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        // Owner-scoped lookup: another shopper's number is invisible here and cannot be deleted.
        var contactNumber = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByIdAndOwnerSpecification(contactNumberId, ownerId), cancellationToken);
        if (contactNumber is null)
        {
            return false;
        }

        await _contactNumbers.DeleteAsync(contactNumber, cancellationToken);
        _logger.LogInformation($"Removed contact number (id {contactNumberId}) for owner {ownerId}.");
        return true;
    }

    private static ContactNumberView ToView(ContactNumber c) => new(c.Id, c.E164Number, c.CreatedDate);
}
