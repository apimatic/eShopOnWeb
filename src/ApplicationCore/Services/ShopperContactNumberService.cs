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
    private readonly IRepository<ShopperContactNumber> _contacts;
    private readonly ITwilioMessagingGateway _messaging;

    public ShopperContactNumberService(
        IRepository<ShopperContactNumber> contacts,
        ITwilioMessagingGateway messaging)
    {
        _contacts = contacts;
        _messaging = messaging;
    }

    public async Task<ShopperContactNumber> RegisterAsync(
        string buyerId,
        string phoneNumber,
        string? countryCode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw new InvalidContactNumberException("A phone number is required.");
        }

        PhoneLookupResult lookup;
        try
        {
            lookup = await _messaging.LookupAsync(phoneNumber.Trim(), countryCode, cancellationToken);
        }
        catch (MessagingProviderException)
        {
            throw new InvalidContactNumberException("The number could not be verified as a usable destination.");
        }

        if (!lookup.IsValid || string.IsNullOrWhiteSpace(lookup.CanonicalNumber))
        {
            throw new InvalidContactNumberException("The number is not a usable destination.");
        }

        var existing = await _contacts.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndCanonicalSpec(buyerId, lookup.CanonicalNumber),
            cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var created = new ShopperContactNumber(buyerId, lookup.CanonicalNumber);
        return await _contacts.AddAsync(created, cancellationToken);
    }

    public async Task<IReadOnlyList<ShopperContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken)
    {
        return await _contacts.ListAsync(new ContactNumbersByBuyerSpec(buyerId), cancellationToken);
    }

    public async Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken)
    {
        var contact = await _contacts.GetByIdAsync(contactNumberId, cancellationToken);
        if (contact is null || contact.BuyerId != buyerId)
        {
            throw new KeyNotFoundException("Contact number was not found.");
        }

        await _contacts.DeleteAsync(contact, cancellationToken);
    }
}
