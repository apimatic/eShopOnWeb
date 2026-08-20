using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IPhoneNumberLookup _lookup;

    public ContactNumberService(IRepository<ContactNumber> contactNumbers, IPhoneNumberLookup lookup)
    {
        _contactNumbers = contactNumbers;
        _lookup = lookup;
    }

    public async Task<ContactNumber> RegisterAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(phoneNumber, nameof(phoneNumber));

        var lookup = await _lookup.LookupAsync(phoneNumber.Trim(), cancellationToken);
        if (!lookup.IsUsable || string.IsNullOrWhiteSpace(lookup.CanonicalNumber))
        {
            throw new InvalidContactNumberException();
        }

        var existing = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndCanonicalSpecification(buyerId, lookup.CanonicalNumber),
            cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var contactNumber = new ContactNumber(buyerId, lookup.CanonicalNumber);
        return await _contactNumbers.AddAsync(contactNumber, cancellationToken);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var contactNumber = await _contactNumbers.GetByIdAsync(contactNumberId, cancellationToken);
        if (contactNumber is null || contactNumber.BuyerId != buyerId)
        {
            throw new ContactNumberNotFoundException();
        }

        await _contactNumbers.DeleteAsync(contactNumber, cancellationToken);
    }
}
