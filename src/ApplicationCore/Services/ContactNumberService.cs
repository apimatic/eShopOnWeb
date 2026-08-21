using System;
using System.Collections.Generic;
using System.Linq;
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

    public async Task<ShopperContactNumber> RegisterAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw new InvalidContactNumberException("A phone number is required.");
        }

        var lookup = await _lookup.LookupAsync(phoneNumber.Trim(), cancellationToken);
        if (!lookup.Valid || string.IsNullOrWhiteSpace(lookup.CanonicalNumber))
        {
            var reasons = lookup.ValidationErrors.Length == 0
                ? "the provider does not consider it a usable destination"
                : string.Join(", ", lookup.ValidationErrors);
            throw new InvalidContactNumberException($"This number cannot be registered: {reasons}.");
        }

        var canonical = lookup.CanonicalNumber;
        var existing = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByCanonicalSpecification(buyerId, canonical), cancellationToken);

        if (existing is not null)
        {
            if (existing.IsDeleted)
            {
                existing.Reactivate(canonical);
                await _contactNumbers.UpdateAsync(existing, cancellationToken);
            }

            return existing;
        }

        var created = new ShopperContactNumber(buyerId, canonical);
        return await _contactNumbers.AddAsync(created, cancellationToken);
    }

    public async Task<IReadOnlyList<ShopperContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return numbers;
    }

    public async Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        var number = await _contactNumbers.GetByIdAsync(contactNumberId, cancellationToken);
        if (number is null || number.BuyerId != buyerId || number.IsDeleted)
        {
            throw new KeyNotFoundException("Contact number was not found.");
        }

        number.Deactivate();
        await _contactNumbers.UpdateAsync(number, cancellationToken);
    }
}
