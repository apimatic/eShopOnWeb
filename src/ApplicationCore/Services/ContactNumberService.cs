using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _repository;
    private readonly IPhoneNumberLookup _lookup;

    public ContactNumberService(IRepository<ContactNumber> repository, IPhoneNumberLookup lookup)
    {
        _repository = repository;
        _lookup = lookup;
    }

    public async Task<ContactNumber> RegisterAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw new InvalidContactNumberException("A phone number is required.");
        }

        var lookup = await _lookup.LookupAsync(phoneNumber.Trim(), cancellationToken);
        if (!lookup.Valid || string.IsNullOrWhiteSpace(lookup.CanonicalNumber))
        {
            throw new InvalidContactNumberException("The provider does not consider this a usable destination number.");
        }

        var canonical = lookup.CanonicalNumber;
        var existing = await _repository.FirstOrDefaultAsync(new ContactNumberByCanonicalSpecification(canonical), cancellationToken);
        if (existing is not null)
        {
            if (existing.BuyerId != buyerId)
            {
                throw new DuplicateException("This number is already registered.");
            }

            return existing;
        }

        var created = new ContactNumber(buyerId, canonical);
        return await _repository.AddAsync(created, cancellationToken);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var numbers = await _repository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return numbers;
    }

    public async Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        var number = await _repository.GetByIdAsync(contactNumberId, cancellationToken);
        if (number is null || number.BuyerId != buyerId)
        {
            throw new ContactNumberNotFoundException(contactNumberId);
        }

        await _repository.DeleteAsync(number, cancellationToken);
    }

    public async Task<ContactNumber?> GetPreferredForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var numbers = await _repository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return numbers.FirstOrDefault();
    }

    public async Task<bool> IsDestinationStillRegisteredAsync(string buyerId, int? contactNumberId, CancellationToken cancellationToken = default)
    {
        if (!contactNumberId.HasValue)
        {
            return false;
        }

        var number = await _repository.GetByIdAsync(contactNumberId.Value, cancellationToken);
        return number is not null && number.BuyerId == buyerId;
    }
}
