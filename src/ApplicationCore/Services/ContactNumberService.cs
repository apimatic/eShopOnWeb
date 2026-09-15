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
    private readonly ITwilioLookupClient _lookupClient;
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        ITwilioLookupClient lookupClient,
        IRepository<ContactNumber> contactNumberRepository,
        IAppLogger<ContactNumberService> logger)
    {
        _lookupClient = lookupClient;
        _contactNumberRepository = contactNumberRepository;
        _logger = logger;
    }

    public async Task<ContactNumber> RegisterAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw new InvalidContactNumberException("A phone number is required.");
        }

        var lookup = await _lookupClient.LookupAsync(phoneNumber.Trim(), cancellationToken);
        if (!lookup.IsValid || string.IsNullOrWhiteSpace(lookup.CanonicalNumber))
        {
            var reason = lookup.ValidationErrors.Count > 0
                ? string.Join(", ", lookup.ValidationErrors)
                : "not a usable destination";
            throw new InvalidContactNumberException(
                $"This number is not a usable destination ({reason}).",
                lookup.ValidationErrors);
        }

        var existing = await _contactNumberRepository.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndPhoneSpecification(buyerId, lookup.CanonicalNumber),
            cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var contact = new ContactNumber(buyerId, lookup.CanonicalNumber, lookup.NationalFormat);
        await _contactNumberRepository.AddAsync(contact, cancellationToken);
        _logger.LogInformation("Registered a contact number for buyer {BuyerId}.", buyerId);
        return contact;
    }

    public async Task<IReadOnlyList<ContactNumber>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var numbers = await _contactNumberRepository.ListAsync(
            new ContactNumbersByBuyerSpecification(buyerId),
            cancellationToken);
        return numbers;
    }

    public async Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        var contact = await _contactNumberRepository.GetByIdAsync(contactNumberId, cancellationToken);
        if (contact is null || contact.BuyerId != buyerId)
        {
            throw new KeyNotFoundException("Contact number was not found.");
        }

        await _contactNumberRepository.DeleteAsync(contact, cancellationToken);
        _logger.LogInformation("Removed contact number {ContactNumberId} for buyer {BuyerId}.", contactNumberId, buyerId);
    }
}
