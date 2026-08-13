using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
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

    public async Task<ContactNumber> RegisterAsync(string ownerId, string rawNumber)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(rawNumber, nameof(rawNumber));

        // The provider is the authoritative judge of whether this is a usable destination, and the
        // owner of the canonical form we store. Reject an unusable number here, not when a send fails.
        var lookup = await _lookup.LookupAsync(rawNumber);
        if (!lookup.IsValid || string.IsNullOrEmpty(lookup.CanonicalE164))
        {
            throw new InvalidContactNumberException(lookup.ValidationErrors);
        }

        // Deduplicate: the same shopper registering the same canonical number twice keeps one entry.
        var existing = await _repository.ListAsync(new ContactNumbersByOwnerSpecification(ownerId));
        var already = existing.FirstOrDefault(c => c.E164Number == lookup.CanonicalE164);
        if (already is not null)
        {
            return already;
        }

        var contactNumber = new ContactNumber(ownerId, lookup.CanonicalE164);
        await _repository.AddAsync(contactNumber);
        _logger.LogInformation("Registered contact number {ContactNumberId} for owner.", contactNumber.Id);
        return contactNumber;
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string ownerId)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        var numbers = await _repository.ListAsync(new ContactNumbersByOwnerSpecification(ownerId));
        return numbers;
    }

    public async Task<bool> DeleteAsync(string ownerId, int contactNumberId)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));

        // Scope to the owner so one shopper can never delete another's number.
        var contactNumber = await _repository.FirstOrDefaultAsync(
            new ContactNumberByOwnerAndIdSpecification(ownerId, contactNumberId));
        if (contactNumber is null)
        {
            return false;
        }

        await _repository.DeleteAsync(contactNumber);
        _logger.LogInformation("Removed contact number {ContactNumberId}.", contactNumberId);
        return true;
    }
}
