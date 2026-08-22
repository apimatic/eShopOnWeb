using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.Messaging;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ShopperContactNumber> _contactNumbers;
    private readonly ITwilioLookupClient _lookupClient;

    public ContactNumberService(
        IRepository<ShopperContactNumber> contactNumbers,
        ITwilioLookupClient lookupClient)
    {
        _contactNumbers = contactNumbers;
        _lookupClient = lookupClient;
    }

    public async Task<ShopperContactNumber> RegisterAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw new InvalidContactNumberException("A phone number is required.");
        }

        PhoneLookupResult lookup;
        try
        {
            lookup = await _lookupClient.LookupAsync(phoneNumber.Trim(), cancellationToken);
        }
        catch (TwilioRequestException)
        {
            throw new InvalidContactNumberException("The provider could not validate the phone number.");
        }

        if (!lookup.IsValid || string.IsNullOrWhiteSpace(lookup.CanonicalNumber))
        {
            throw new InvalidContactNumberException("The provider does not consider this a usable destination.");
        }

        var existing = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByCanonicalSpec(buyerId, lookup.CanonicalNumber), cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        var contact = new ShopperContactNumber(buyerId, lookup.CanonicalNumber);
        return await _contactNumbers.AddAsync(contact, cancellationToken);
    }

    public async Task<IReadOnlyList<ShopperContactNumber>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpec(buyerId), cancellationToken);
        return numbers;
    }

    public async Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        var contact = await _contactNumbers.GetByIdAsync(contactNumberId, cancellationToken);
        if (contact is null || contact.BuyerId != buyerId)
        {
            throw new ContactNumberNotFoundException();
        }

        await _contactNumbers.DeleteAsync(contact, cancellationToken);
    }
}
