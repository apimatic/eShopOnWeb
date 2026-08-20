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
    private readonly IOrderMessagingGateway _messaging;

    public ContactNumberService(
        IRepository<ShopperContactNumber> contactNumbers,
        IOrderMessagingGateway messaging)
    {
        _contactNumbers = contactNumbers;
        _messaging = messaging;
    }

    public async Task<ShopperContactNumber> RegisterAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw new InvalidContactNumberException("A mobile number is required.");
        }

        PhoneNumberLookup lookup;
        try
        {
            lookup = await _messaging.LookupAsync(phoneNumber.Trim(), cancellationToken);
        }
        catch (OrderMessagingException ex) when (ex.IsCallerFault)
        {
            throw new InvalidContactNumberException("The provider does not consider this a usable destination.");
        }

        if (!lookup.Valid || lookup.ValidationErrors.Count > 0 || string.IsNullOrWhiteSpace(lookup.CanonicalNumber))
        {
            throw new InvalidContactNumberException("The provider does not consider this a usable destination.");
        }

        var existing = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndCanonicalSpecification(buyerId, lookup.CanonicalNumber),
            cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var contact = new ShopperContactNumber(buyerId, lookup.CanonicalNumber, lookup.LineType);
        return await _contactNumbers.AddAsync(contact, cancellationToken);
    }

    public async Task<IReadOnlyList<ShopperContactNumber>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken)
    {
        var items = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return items;
    }

    public async Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken)
    {
        var contact = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndIdSpecification(buyerId, contactNumberId),
            cancellationToken);
        if (contact is null)
        {
            throw new KeyNotFoundException("Contact number was not found.");
        }

        await _contactNumbers.DeleteAsync(contact, cancellationToken);
    }
}
