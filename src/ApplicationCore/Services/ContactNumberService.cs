using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly ISmsGateway _smsGateway;

    public ContactNumberService(IRepository<ContactNumber> contactNumbers, ISmsGateway smsGateway)
    {
        _contactNumbers = contactNumbers;
        _smsGateway = smsGateway;
    }

    public async Task<ContactNumber> RegisterAsync(string buyerId, string phoneNumber, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw new UnusableDestinationException("A destination number is required.");
        }

        var lookup = await _smsGateway.LookupAsync(phoneNumber.Trim(), ct);
        if (!lookup.ProviderReached)
        {
            if (lookup.HttpStatus is 404 or 400)
            {
                throw new UnusableDestinationException("The number is not a usable destination.");
            }

            throw new ProviderUnavailableException("The messaging provider could not look up the number.");
        }

        if (!lookup.IsUsable || string.IsNullOrWhiteSpace(lookup.CanonicalNumber))
        {
            throw new UnusableDestinationException("The number is not a usable destination.");
        }

        var existing = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndCanonicalSpec(buyerId, lookup.CanonicalNumber), ct);
        if (existing is not null)
        {
            throw new DuplicateException("That destination is already registered.");
        }

        var contact = new ContactNumber(buyerId, lookup.CanonicalNumber);
        return await _contactNumbers.AddAsync(contact, ct);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken ct)
    {
        return await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpec(buyerId), ct);
    }

    public async Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken ct)
    {
        var contact = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndIdSpec(buyerId, contactNumberId), ct);
        if (contact is null)
        {
            throw new NotificationNotFoundException("Contact number was not found.");
        }

        await _contactNumbers.DeleteAsync(contact, ct);
    }
}
