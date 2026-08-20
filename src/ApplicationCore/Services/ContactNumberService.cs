using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _repository;
    private readonly ITwilioLookupClient _lookupClient;

    public ContactNumberService(IRepository<ContactNumber> repository, ITwilioLookupClient lookupClient)
    {
        _repository = repository;
        _lookupClient = lookupClient;
    }

    public async Task<ContactNumber> RegisterAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw new BadRequestException("A phone number is required.");
        }

        PhoneNumberLookupResult lookup;
        try
        {
            lookup = await _lookupClient.LookupAsync(phoneNumber.Trim(), cancellationToken);
        }
        catch (ProviderException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new ProviderException("The phone number could not be validated with the messaging provider.");
        }

        if (!lookup.Valid || string.IsNullOrWhiteSpace(lookup.CanonicalNumber))
        {
            throw new BadRequestException("The phone number is not a usable destination.");
        }

        var existing = await _repository.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndCanonicalSpecification(buyerId, lookup.CanonicalNumber),
            cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        var contactNumber = new ContactNumber(buyerId, lookup.CanonicalNumber);
        return await _repository.AddAsync(contactNumber, cancellationToken);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _repository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        var contactNumber = await _repository.GetByIdAsync(contactNumberId, cancellationToken);
        if (contactNumber == null || contactNumber.BuyerId != buyerId)
        {
            throw new EntityNotFoundException("Contact number was not found.");
        }

        await _repository.DeleteAsync(contactNumber, cancellationToken);
    }
}
