using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _repository;
    private readonly IMessagingProvider _provider;

    public ContactNumberService(IRepository<ContactNumber> repository, IMessagingProvider provider)
    {
        _repository = repository;
        _provider = provider;
    }

    public async Task<RegisterContactNumberResult> RegisterAsync(string ownerId, string rawPhoneNumber, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));

        if (string.IsNullOrWhiteSpace(rawPhoneNumber))
            return RegisterContactNumberResult.Rejected("A phone number is required.");

        // Reject an unusable destination now, at registration, rather than when a later message fails.
        var lookup = await _provider.LookupAsync(rawPhoneNumber, cancellationToken);
        if (!lookup.IsValid || string.IsNullOrEmpty(lookup.E164))
            return RegisterContactNumberResult.Rejected("The provider does not consider this a usable destination number.");

        var canonical = lookup.E164;

        // Store the provider's canonical form; keep a shopper's set of numbers free of duplicates.
        var existing = await _repository.FirstOrDefaultAsync(
            new ContactNumberByOwnerAndNumberSpecification(ownerId, canonical), cancellationToken);
        if (existing is not null)
            return RegisterContactNumberResult.Ok(existing);

        var contactNumber = new ContactNumber(ownerId, canonical);
        await _repository.AddAsync(contactNumber, cancellationToken);
        return RegisterContactNumberResult.Ok(contactNumber);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        var numbers = await _repository.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), cancellationToken);
        return numbers;
    }

    public async Task<bool> DeleteAsync(string ownerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        var number = await _repository.GetByIdAsync(contactNumberId, cancellationToken);

        // A number belongs to the shopper who registered it: never reveal or delete another's.
        if (number is null || number.OwnerId != ownerId)
            return false;

        await _repository.DeleteAsync(number, cancellationToken);
        return true;
    }
}
