using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Messaging;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _repository;
    private readonly ISmsProvider _smsProvider;

    public ContactNumberService(IRepository<ContactNumber> repository, ISmsProvider smsProvider)
    {
        _repository = repository;
        _smsProvider = smsProvider;
    }

    public async Task<ContactNumber> RegisterAsync(string ownerId, string rawNumber, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(rawNumber, nameof(rawNumber));

        // Reject an unusable destination here, at registration, rather than when a message later fails.
        var lookup = await _smsProvider.LookupAsync(rawNumber, cancellationToken);
        if (!lookup.IsValid || string.IsNullOrEmpty(lookup.CanonicalE164))
            throw new InvalidPhoneNumberException();

        // Store the provider's canonical form, not whatever the caller typed.
        var contactNumber = new ContactNumber(ownerId, lookup.CanonicalE164);
        return await _repository.AddAsync(contactNumber, cancellationToken);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        return await _repository.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), cancellationToken);
    }

    public async Task<bool> RemoveAsync(string ownerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));

        // Scoped by owner: a number that is not the caller's simply is not found for them.
        var contactNumber = await _repository.FirstOrDefaultAsync(
            new ContactNumberByIdForOwnerSpecification(ownerId, contactNumberId), cancellationToken);
        if (contactNumber is null)
            return false;

        await _repository.DeleteAsync(contactNumber, cancellationToken);
        return true;
    }
}
