using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ShopperContactNumber> _contactNumbers;
    private readonly IPhoneNumberLookup _lookup;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ShopperContactNumber> contactNumbers,
        IPhoneNumberLookup lookup,
        IAppLogger<ContactNumberService> logger)
    {
        _contactNumbers = contactNumbers;
        _lookup = lookup;
        _logger = logger;
    }

    public async Task<ShopperContactNumber> RegisterAsync(string buyerId, string rawNumber, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawNumber))
        {
            throw new ContactNumberRejectedException("A mobile number is required.");
        }

        var lookup = await _lookup.LookupAsync(rawNumber.Trim(), cancellationToken);
        if (!lookup.IsUsable || string.IsNullOrWhiteSpace(lookup.CanonicalNumber))
        {
            throw new ContactNumberRejectedException(
                lookup.Reason ?? "The provider does not consider this a usable destination.");
        }

        var canonical = lookup.CanonicalNumber;
        var existing = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndCanonicalSpecification(buyerId, canonical),
            cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var entity = new ShopperContactNumber(buyerId, canonical);
        await _contactNumbers.AddAsync(entity, cancellationToken);
        _logger.LogInformation("Registered a contact number for buyer {BuyerId}", buyerId);
        return entity;
    }

    public async Task<IReadOnlyList<ShopperContactNumber>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return numbers;
    }

    public async Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken)
    {
        var entity = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByIdAndBuyerSpecification(contactNumberId, buyerId),
            cancellationToken);
        if (entity is null)
        {
            return false;
        }

        await _contactNumbers.DeleteAsync(entity, cancellationToken);
        _logger.LogInformation("Removed contact number {ContactNumberId} for buyer {BuyerId}", contactNumberId, buyerId);
        return true;
    }
}
