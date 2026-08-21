using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ShopperContactNumberService : IShopperContactNumberService
{
    private readonly IRepository<ShopperContactNumber> _contactNumbers;
    private readonly IPhoneNumberLookupClient _lookupClient;
    private readonly IAppLogger<ShopperContactNumberService> _logger;

    public ShopperContactNumberService(
        IRepository<ShopperContactNumber> contactNumbers,
        IPhoneNumberLookupClient lookupClient,
        IAppLogger<ShopperContactNumberService> logger)
    {
        _contactNumbers = contactNumbers;
        _lookupClient = lookupClient;
        _logger = logger;
    }

    public async Task<ShopperContactNumber> RegisterAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw new UnusableDestinationException("A mobile number is required.");
        }

        var lookup = await _lookupClient.LookupAsync(phoneNumber, cancellationToken);
        if (!lookup.IsValid || string.IsNullOrEmpty(lookup.CanonicalNumber))
        {
            throw new UnusableDestinationException("The number is not a usable destination.");
        }

        var existingSpec = new ShopperContactNumberByCanonicalSpecification(buyerId, lookup.CanonicalNumber);
        var existing = await _contactNumbers.FirstOrDefaultAsync(existingSpec, cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        var contact = new ShopperContactNumber(buyerId, lookup.CanonicalNumber);
        await _contactNumbers.AddAsync(contact, cancellationToken);
        _logger.LogInformation("Registered contact number {ContactNumberId} for buyer.", contact.Id);
        return contact;
    }

    public async Task<IReadOnlyList<ShopperContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var spec = new ShopperContactNumbersSpecification(buyerId);
        return await _contactNumbers.ListAsync(spec, cancellationToken);
    }

    public async Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        var spec = new ShopperContactNumberByIdSpecification(buyerId, contactNumberId);
        var contact = await _contactNumbers.FirstOrDefaultAsync(spec, cancellationToken);
        if (contact is null)
        {
            throw new ContactNumberNotFoundException(contactNumberId);
        }

        await _contactNumbers.DeleteAsync(contact, cancellationToken);
        _logger.LogInformation("Removed contact number {ContactNumberId} for buyer.", contactNumberId);
    }
}
