using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
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

    public async Task<ContactNumber> RegisterAsync(string buyerId, string phoneNumber, string? countryCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw new InvalidContactNumberException("A phone number is required.");
        }

        var lookup = await _lookupClient.LookupAsync(phoneNumber.Trim(), countryCode, cancellationToken);
        if (!lookup.Valid || string.IsNullOrWhiteSpace(lookup.CanonicalPhoneNumber))
        {
            var reasons = lookup.ValidationErrors.Count > 0
                ? string.Join(", ", lookup.ValidationErrors)
                : "NOT_A_USABLE_DESTINATION";
            throw new InvalidContactNumberException($"The phone number is not a usable destination ({reasons}).");
        }

        var existingSpec = new CustomerContactNumberByCanonicalSpecification(buyerId, lookup.CanonicalPhoneNumber);
        var existing = await _contactNumberRepository.FirstOrDefaultAsync(existingSpec, cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        var contact = new ContactNumber(buyerId, lookup.CanonicalPhoneNumber, lookup.NationalFormat, lookup.CountryCode);
        return await _contactNumberRepository.AddAsync(contact, cancellationToken);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var spec = new CustomerContactNumbersSpecification(buyerId);
        return await _contactNumberRepository.ListAsync(spec, cancellationToken);
    }

    public async Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        var contact = await _contactNumberRepository.GetByIdAsync(contactNumberId, cancellationToken);
        if (contact == null || contact.BuyerId != buyerId)
        {
            throw new ResourceNotFoundException("Contact number not found.");
        }

        await _contactNumberRepository.DeleteAsync(contact, cancellationToken);
    }

    public async Task<ContactNumber?> GetActiveForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var numbers = await ListForBuyerAsync(buyerId, cancellationToken);
        return numbers.FirstOrDefault();
    }
}
