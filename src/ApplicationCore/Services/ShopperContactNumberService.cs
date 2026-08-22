using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ShopperContactNumberService : IShopperContactNumberService
{
    private readonly IRepository<ShopperContactNumber> _contactNumbers;
    private readonly ISmsGateway _smsGateway;

    public ShopperContactNumberService(
        IRepository<ShopperContactNumber> contactNumbers,
        ISmsGateway smsGateway)
    {
        _contactNumbers = contactNumbers;
        _smsGateway = smsGateway;
    }

    public async Task<ShopperContactNumber> RegisterAsync(
        string buyerId,
        string phoneNumber,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw new InvalidContactNumberException("A phone number is required.");
        }

        PhoneNumberLookupResult lookup;
        try
        {
            lookup = await _smsGateway.LookupNumberAsync(phoneNumber.Trim(), cancellationToken);
        }
        catch (SmsGatewayException)
        {
            throw;
        }

        if (!lookup.IsUsable || string.IsNullOrWhiteSpace(lookup.CanonicalNumber))
        {
            throw new InvalidContactNumberException(
                "The provider does not consider this number a usable destination.");
        }

        var existing = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndCanonicalSpecification(buyerId, lookup.CanonicalNumber),
            cancellationToken);
        if (existing is not null)
        {
            throw new DuplicateException("This number is already registered.");
        }

        var contact = new ShopperContactNumber(buyerId, lookup.CanonicalNumber);
        return await _contactNumbers.AddAsync(contact, cancellationToken);
    }

    public async Task<IReadOnlyList<ShopperContactNumber>> ListForBuyerAsync(
        string buyerId,
        CancellationToken cancellationToken)
    {
        return await _contactNumbers.ListAsync(
            new ContactNumbersByBuyerSpecification(buyerId),
            cancellationToken);
    }

    public async Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken)
    {
        var contact = await _contactNumbers.GetByIdAsync(contactNumberId, cancellationToken);
        if (contact is null || !contact.BelongsTo(buyerId))
        {
            throw new ContactNumberNotFoundException();
        }

        await _contactNumbers.DeleteAsync(contact, cancellationToken);
    }
}
