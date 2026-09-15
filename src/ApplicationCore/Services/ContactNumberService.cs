using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly ITwilioLookupClient _lookupClient;

    public ContactNumberService(IRepository<ContactNumber> contactNumbers, ITwilioLookupClient lookupClient)
    {
        _contactNumbers = contactNumbers;
        _lookupClient = lookupClient;
    }

    public async Task<ContactNumber> RegisterAsync(string buyerId, string phoneNumber, string? countryCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw new InvalidContactNumberException("A phone number is required.");
        }

        var lookup = await _lookupClient.LookupAsync(phoneNumber.Trim(), countryCode, cancellationToken);
        if (!lookup.Valid || string.IsNullOrWhiteSpace(lookup.CanonicalPhoneNumber))
        {
            var reasons = lookup.ValidationErrors.Count > 0
                ? string.Join(", ", lookup.ValidationErrors)
                : "NOT_A_USABLE_DESTINATION";
            throw new InvalidContactNumberException($"The number is not a usable destination: {reasons}.");
        }

        var existing = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndCanonicalNumberSpecification(buyerId, lookup.CanonicalPhoneNumber),
            cancellationToken);
        if (existing is not null)
        {
            throw new DuplicateException("This number is already registered.");
        }

        var contact = new ContactNumber(buyerId, lookup.CanonicalPhoneNumber, lookup.NationalFormat, lookup.CountryCode);
        return await _contactNumbers.AddAsync(contact, cancellationToken);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerIdSpecification(buyerId), cancellationToken);
        return numbers;
    }

    public async Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        var contact = await _contactNumbers.GetByIdAsync(contactNumberId, cancellationToken);
        if (contact is null || contact.BuyerId != buyerId)
        {
            return false;
        }

        await _contactNumbers.DeleteAsync(contact, cancellationToken);
        return true;
    }

    public async Task<ContactNumber?> GetLatestForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerIdSpecification(buyerId), cancellationToken);
        return numbers.FirstOrDefault();
    }
}
