using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ShopperContactService : IShopperContactService
{
    private readonly IRepository<ShopperContactNumber> _contacts;
    private readonly IMessagingGateway _messaging;

    public ShopperContactService(IRepository<ShopperContactNumber> contacts, IMessagingGateway messaging)
    {
        _contacts = contacts;
        _messaging = messaging;
    }

    public async Task<ShopperContactNumber> RegisterAsync(string buyerId, string phoneNumber, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw new InvalidContactNumberException("A phone number is required.");
        }

        var lookup = await _messaging.LookupNumberAsync(phoneNumber.Trim(), ct);
        if (!lookup.IsUsable || string.IsNullOrWhiteSpace(lookup.CanonicalNumber))
        {
            throw new InvalidContactNumberException(
                lookup.RejectionReason ?? "The provider does not consider this number a usable destination.");
        }

        var existing = await _contacts.FirstOrDefaultAsync(
            new ShopperContactNumberByCanonicalSpecification(buyerId, lookup.CanonicalNumber), ct);
        if (existing is not null)
        {
            return existing;
        }

        var contact = new ShopperContactNumber(buyerId, lookup.CanonicalNumber);
        return await _contacts.AddAsync(contact, ct);
    }

    public async Task<IReadOnlyList<ShopperContactNumber>> ListAsync(string buyerId, CancellationToken ct)
    {
        return await _contacts.ListAsync(new ShopperContactNumbersSpecification(buyerId), ct);
    }

    public async Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken ct)
    {
        var contact = await _contacts.FirstOrDefaultAsync(
            new ShopperContactNumberByIdSpecification(contactNumberId, buyerId), ct);
        if (contact is null)
        {
            throw new OrderNotificationException("Contact number not found.", 404);
        }

        await _contacts.DeleteAsync(contact, ct);
    }
}
