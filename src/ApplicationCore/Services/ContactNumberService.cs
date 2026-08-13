using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _repository;
    private readonly ISmsGateway _smsGateway;

    public ContactNumberService(IRepository<ContactNumber> repository, ISmsGateway smsGateway)
    {
        _repository = repository;
        _smsGateway = smsGateway;
    }

    public async Task<RegisterContactNumberResult> RegisterAsync(string ownerId, string rawNumber, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(rawNumber, nameof(rawNumber));

        // Reject an unusable destination here, up front — not later when a message fails to go out.
        var lookup = await _smsGateway.LookupAsync(rawNumber, cancellationToken);
        if (!lookup.IsValid || string.IsNullOrEmpty(lookup.CanonicalNumber))
        {
            return RegisterContactNumberResult.Rejected(
                lookup.Reason ?? "The number is not a usable destination.");
        }

        var canonical = lookup.CanonicalNumber;

        // Store the provider's canonical form. If the shopper already has this number, keep the one on file.
        var existing = await _repository.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), cancellationToken);
        var already = existing.FirstOrDefault(c => c.PhoneNumber == canonical);
        if (already is not null)
        {
            return RegisterContactNumberResult.Ok(already);
        }

        var contactNumber = new ContactNumber(ownerId, canonical);
        await _repository.AddAsync(contactNumber, cancellationToken);
        return RegisterContactNumberResult.Ok(contactNumber);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        return await _repository.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), cancellationToken);
    }

    public async Task<bool> DeleteAsync(string ownerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));

        var contactNumber = await _repository.FirstOrDefaultAsync(
            new ContactNumberByIdForOwnerSpecification(contactNumberId, ownerId), cancellationToken);
        if (contactNumber is null)
        {
            return false; // not the caller's, or not found — either way, nothing to remove
        }

        await _repository.DeleteAsync(contactNumber, cancellationToken);
        return true;
    }
}
