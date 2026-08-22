using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly ITwilioLookupClient _lookupClient;

    public ContactNumberService(
        IRepository<ContactNumber> contactNumberRepository,
        ITwilioLookupClient lookupClient)
    {
        _contactNumberRepository = contactNumberRepository;
        _lookupClient = lookupClient;
    }

    public async Task<ContactNumber> RegisterAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw new InvalidContactNumberException("A mobile number is required.");
        }

        var lookup = await _lookupClient.LookupAsync(phoneNumber.Trim(), cancellationToken);
        if (!lookup.Valid || string.IsNullOrWhiteSpace(lookup.CanonicalPhoneNumber))
        {
            throw new InvalidContactNumberException(
                "The provider does not consider this a usable destination number.");
        }

        var existingSpec = new ContactNumberByBuyerAndPhoneSpecification(buyerId, lookup.CanonicalPhoneNumber);
        var existing = await _contactNumberRepository.FirstOrDefaultAsync(existingSpec, cancellationToken);
        if (existing is not null)
        {
            if (!existing.IsActive)
            {
                existing.Reactivate();
                await _contactNumberRepository.UpdateAsync(existing, cancellationToken);
            }

            return existing;
        }

        var contact = new ContactNumber(buyerId, lookup.CanonicalPhoneNumber);
        return await _contactNumberRepository.AddAsync(contact, cancellationToken);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var spec = new ContactNumbersByBuyerSpecification(buyerId, activeOnly: true);
        var numbers = await _contactNumberRepository.ListAsync(spec, cancellationToken);
        return numbers;
    }

    public async Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        var contact = await _contactNumberRepository.GetByIdAsync(contactNumberId, cancellationToken);
        if (contact is null || contact.BuyerId != buyerId || !contact.IsActive)
        {
            throw new KeyNotFoundException("Contact number was not found.");
        }

        contact.Deactivate();
        await _contactNumberRepository.UpdateAsync(contact, cancellationToken);
    }
}
