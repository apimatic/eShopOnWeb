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
    private readonly ISmsNotificationGateway _smsGateway;

    public ShopperContactNumberService(
        IRepository<ShopperContactNumber> contactNumbers,
        ISmsNotificationGateway smsGateway)
    {
        _contactNumbers = contactNumbers;
        _smsGateway = smsGateway;
    }

    public async Task<ShopperContactNumber> RegisterAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw new UnusableContactNumberException("A phone number is required.");
        }

        var lookup = await _smsGateway.LookupAsync(phoneNumber.Trim(), cancellationToken);
        if (!lookup.IsUsable || string.IsNullOrWhiteSpace(lookup.CanonicalNumber))
        {
            throw new UnusableContactNumberException(
                lookup.RejectionReason ?? "The number is not a usable destination.");
        }

        var existing = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndCanonicalSpecification(buyerId, lookup.CanonicalNumber),
            cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var created = new ShopperContactNumber(buyerId, lookup.CanonicalNumber);
        return await _contactNumbers.AddAsync(created, cancellationToken);
    }

    public async Task<IReadOnlyList<ShopperContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken)
    {
        return await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken)
    {
        var existing = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByIdAndBuyerSpecification(contactNumberId, buyerId),
            cancellationToken);
        if (existing is null)
        {
            return false;
        }

        await _contactNumbers.DeleteAsync(existing, cancellationToken);
        return true;
    }
}
