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
    private readonly IRepository<ContactNumber> _repository;
    private readonly ITwilioLookupClient _lookupClient;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ContactNumber> repository,
        ITwilioLookupClient lookupClient,
        IAppLogger<ContactNumberService> logger)
    {
        _repository = repository;
        _lookupClient = lookupClient;
        _logger = logger;
    }

    public async Task<ContactNumber> RegisterAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw new InvalidContactNumberException("A phone number is required.");
        }

        try
        {
            var lookup = await _lookupClient.LookupPhoneNumberAsync(phoneNumber, cancellationToken);
            if (!lookup.Valid || string.IsNullOrWhiteSpace(lookup.PhoneNumber))
            {
                throw new InvalidContactNumberException("The provider does not consider this number a usable destination.");
            }

            var existing = await _repository.FirstOrDefaultAsync(
                new ContactNumberByBuyerAndCanonicalSpecification(buyerId, lookup.PhoneNumber),
                cancellationToken);
            if (existing != null)
            {
                return existing;
            }

            var contact = new ContactNumber(buyerId, lookup.PhoneNumber);
            return await _repository.AddAsync(contact, cancellationToken);
        }
        catch (InvalidContactNumberException)
        {
            throw;
        }
        catch (TwilioApiException ex)
        {
            _logger.LogWarning("Contact number lookup failed for buyer {BuyerId}: {Message}", buyerId, ex.Message);
            throw new InvalidContactNumberException("The number could not be validated with the messaging provider.");
        }
    }

    public async Task<IReadOnlyList<ContactNumber>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _repository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        var contact = await _repository.GetByIdAsync(contactNumberId, cancellationToken);
        if (contact == null || contact.BuyerId != buyerId)
        {
            throw new KeyNotFoundException("Contact number was not found.");
        }

        await _repository.DeleteAsync(contact, cancellationToken);
    }
}
