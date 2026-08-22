using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IPhoneNumberLookup _phoneNumberLookup;

    public ContactNumberService(IRepository<ContactNumber> contactNumbers, IPhoneNumberLookup phoneNumberLookup)
    {
        _contactNumbers = contactNumbers;
        _phoneNumberLookup = phoneNumberLookup;
    }

    public async Task<ContactNumber> RegisterAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw new InvalidContactNumberException("A phone number is required.");
        }

        var lookup = await _phoneNumberLookup.LookupAsync(phoneNumber.Trim(), cancellationToken);
        if (!lookup.IsValid || string.IsNullOrWhiteSpace(lookup.CanonicalNumber))
        {
            throw new InvalidContactNumberException("The phone number is not a usable destination.");
        }

        var existing = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndCanonicalSpecification(buyerId, lookup.CanonicalNumber),
            cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        var contactNumber = new ContactNumber(buyerId, lookup.CanonicalNumber, lookup.CountryCode);
        return await _contactNumbers.AddAsync(contactNumber, cancellationToken);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return numbers;
    }

    public async Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        var contactNumber = await _contactNumbers.GetByIdAsync(contactNumberId, cancellationToken);
        if (contactNumber == null || contactNumber.BuyerId != buyerId)
        {
            throw new ContactNumberNotFoundException();
        }

        await _contactNumbers.DeleteAsync(contactNumber, cancellationToken);
    }
}
