using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _repository;
    private readonly IPhoneNumberLookup _lookup;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ContactNumber> repository,
        IPhoneNumberLookup lookup,
        IAppLogger<ContactNumberService> logger)
    {
        _repository = repository;
        _lookup = lookup;
        _logger = logger;
    }

    public async Task<RegisterContactNumberResult> RegisterAsync(string ownerId, string rawPhoneNumber, CancellationToken cancellationToken = default)
    {
        // Reject a number the provider does not consider a usable destination here, up front — not later
        // when a message fails to go out. Store the provider's canonical form, not whatever was typed.
        var lookup = await _lookup.LookupAsync(rawPhoneNumber, cancellationToken);
        if (!lookup.IsValid || string.IsNullOrEmpty(lookup.CanonicalE164))
        {
            _logger.LogWarning($"Rejected a contact number registration for owner {ownerId}: not a usable destination.");
            return RegisterContactNumberResult.Rejected(
                "The number is not a usable SMS destination.", lookup.ValidationErrors);
        }

        var canonical = lookup.CanonicalE164!;

        // Don't store the same canonical number twice for one shopper.
        var existing = await _repository.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), cancellationToken);
        var duplicate = existing.FirstOrDefault(c => c.PhoneNumber == canonical);
        if (duplicate is not null)
            return RegisterContactNumberResult.Ok(duplicate);

        var contactNumber = new ContactNumber(ownerId, canonical);
        await _repository.AddAsync(contactNumber, cancellationToken);
        _logger.LogInformation($"Registered a contact number for owner {ownerId} (id {contactNumber.Id}).");
        return RegisterContactNumberResult.Ok(contactNumber);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        return await _repository.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), cancellationToken);
    }

    public async Task<bool> RemoveAsync(string ownerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        var contactNumber = await _repository.FirstOrDefaultAsync(
            new ContactNumberByIdForOwnerSpecification(contactNumberId, ownerId), cancellationToken);
        if (contactNumber is null)
            return false;

        await _repository.DeleteAsync(contactNumber, cancellationToken);
        _logger.LogInformation($"Removed contact number {contactNumberId} for owner {ownerId}.");
        return true;
    }
}
