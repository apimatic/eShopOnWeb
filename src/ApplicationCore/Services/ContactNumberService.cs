using System.Collections.Generic;
using System.Linq;
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
    private readonly ITwilioLookupClient _lookupClient;

    public ContactNumberService(IRepository<ContactNumber> contactNumbers, ITwilioLookupClient lookupClient)
    {
        _contactNumbers = contactNumbers;
        _lookupClient = lookupClient;
    }

    public async Task<ContactNumberRecord> RegisterAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken = default)
    {
        var lookup = await _lookupClient.LookupAsync(phoneNumber, cancellationToken);
        if (!lookup.Valid || string.IsNullOrWhiteSpace(lookup.CanonicalNumber))
        {
            throw new InvalidContactNumberException();
        }

        var existing = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndCanonicalSpecification(buyerId, lookup.CanonicalNumber),
            cancellationToken);
        if (existing != null)
        {
            return ToRecord(existing);
        }

        var contact = new ContactNumber(buyerId, lookup.CanonicalNumber);
        await _contactNumbers.AddAsync(contact, cancellationToken);
        return ToRecord(contact);
    }

    public async Task<IReadOnlyList<ContactNumberRecord>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var contacts = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return contacts.Select(ToRecord).ToList();
    }

    public async Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        var contact = await _contactNumbers.GetByIdAsync(contactNumberId, cancellationToken);
        if (contact == null || contact.BuyerId != buyerId)
        {
            return false;
        }

        await _contactNumbers.DeleteAsync(contact, cancellationToken);
        return true;
    }

    private static ContactNumberRecord ToRecord(ContactNumber contact) =>
        new(contact.Id, contact.CanonicalNumber, contact.CreatedAt);
}
