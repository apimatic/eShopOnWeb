using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ShopperContactService : IShopperContactService
{
    private readonly IRepository<ShopperContactNumber> _contactNumbers;
    private readonly ISmsGateway _smsGateway;

    public ShopperContactService(
        IRepository<ShopperContactNumber> contactNumbers,
        ISmsGateway smsGateway)
    {
        _contactNumbers = contactNumbers;
        _smsGateway = smsGateway;
    }

    public async Task<ShopperContactNumber> RegisterAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw new InvalidContactNumberException();
        }

        var lookup = await _smsGateway.LookupAsync(phoneNumber.Trim(), cancellationToken);
        if (!lookup.IsUsable || string.IsNullOrWhiteSpace(lookup.CanonicalNumber))
        {
            throw new InvalidContactNumberException();
        }

        var existing = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndCanonicalSpec(buyerId, lookup.CanonicalNumber),
            cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var contact = new ShopperContactNumber(buyerId, lookup.CanonicalNumber);
        return await _contactNumbers.AddAsync(contact, cancellationToken);
    }

    public async Task<IReadOnlyList<ShopperContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken)
    {
        return await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpec(buyerId), cancellationToken);
    }

    public async Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken)
    {
        var contact = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByIdAndBuyerSpec(contactNumberId, buyerId),
            cancellationToken);
        if (contact is null)
        {
            return false;
        }

        await _contactNumbers.DeleteAsync(contact, cancellationToken);
        return true;
    }
}
