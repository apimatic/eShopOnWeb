using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ShopperContactNumber> _contactNumbers;
    private readonly IPhoneNumberLookup _lookup;

    public ContactNumberService(IRepository<ShopperContactNumber> contactNumbers, IPhoneNumberLookup lookup)
    {
        _contactNumbers = contactNumbers;
        _lookup = lookup;
    }

    public async Task<ShopperContactNumber> RegisterAsync(string buyerId, string phoneNumber, string? countryCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw new InvalidContactNumberException("A phone number is required.");
        }

        var lookedUp = await _lookup.LookupAsync(phoneNumber.Trim(), string.IsNullOrWhiteSpace(countryCode) ? null : countryCode.Trim(), cancellationToken);
        if (!lookedUp.Valid || string.IsNullOrWhiteSpace(lookedUp.PhoneNumber))
        {
            throw new InvalidContactNumberException(
                "The phone number is not a usable destination.",
                lookedUp.ValidationErrors);
        }

        var existing = await _contactNumbers.FirstOrDefaultAsync(new ContactNumberByBuyerAndPhoneSpec(buyerId, lookedUp.PhoneNumber), cancellationToken);
        if (existing != null)
        {
            throw new DuplicateException("This phone number is already registered.");
        }

        var contact = new ShopperContactNumber(buyerId, lookedUp.PhoneNumber, lookedUp.NationalFormat, lookedUp.CountryCode);
        return await _contactNumbers.AddAsync(contact, cancellationToken);
    }

    public async Task<IReadOnlyList<ShopperContactNumber>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _contactNumbers.ListAsync(new ContactNumbersByBuyerIdSpec(buyerId), cancellationToken);
    }

    public async Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        var contact = await _contactNumbers.FirstOrDefaultAsync(new ContactNumberByIdAndBuyerSpec(contactNumberId, buyerId), cancellationToken);
        if (contact == null)
        {
            throw new EntityNotFoundException("Contact number not found.");
        }

        await _contactNumbers.DeleteAsync(contact, cancellationToken);
    }

    public async Task<ShopperContactNumber?> GetPrimaryForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerIdSpec(buyerId), cancellationToken);
        return numbers.Count == 0 ? null : numbers[0];
    }

    public async Task<bool> IsStillRegisteredAsync(string buyerId, string canonicalPhoneNumber, CancellationToken cancellationToken = default)
    {
        var existing = await _contactNumbers.FirstOrDefaultAsync(new ContactNumberByBuyerAndPhoneSpec(buyerId, canonicalPhoneNumber), cancellationToken);
        return existing != null;
    }
}
