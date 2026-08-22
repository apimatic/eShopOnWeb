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
    private readonly IRepository<ShopperContactNumber> _repository;
    private readonly ITwilioLookupClient _lookupClient;
    private readonly IAppLogger<ShopperContactNumberService> _logger;

    public ShopperContactNumberService(
        IRepository<ShopperContactNumber> repository,
        ITwilioLookupClient lookupClient,
        IAppLogger<ShopperContactNumberService> logger)
    {
        _repository = repository;
        _lookupClient = lookupClient;
        _logger = logger;
    }

    public async Task<ShopperContactNumber> RegisterAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw new InvalidContactNumberException("A phone number is required.");
        }

        PhoneNumberLookupResult lookup;
        try
        {
            lookup = await _lookupClient.LookupAsync(phoneNumber.Trim(), cancellationToken);
        }
        catch (System.Exception ex)
        {
            _logger.LogWarning("Phone number lookup failed: {Message}", ex.Message);
            throw new InvalidContactNumberException("The phone number could not be validated with the messaging provider.");
        }

        if (!lookup.Valid || string.IsNullOrWhiteSpace(lookup.CanonicalPhoneNumber))
        {
            throw new InvalidContactNumberException("The phone number is not a usable destination.");
        }

        var existing = await _repository.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndCanonicalSpec(buyerId, lookup.CanonicalPhoneNumber),
            cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        var created = new ShopperContactNumber(buyerId, lookup.CanonicalPhoneNumber);
        return await _repository.AddAsync(created, cancellationToken);
    }

    public async Task<IReadOnlyList<ShopperContactNumber>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _repository.ListAsync(new ContactNumbersByBuyerSpec(buyerId), cancellationToken);
    }

    public async Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        var existing = await _repository.GetByIdAsync(contactNumberId, cancellationToken);
        if (existing == null || existing.BuyerId != buyerId)
        {
            throw new ContactNumberNotFoundException();
        }

        await _repository.DeleteAsync(existing, cancellationToken);
    }

    public async Task<ShopperContactNumber?> GetLatestForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var numbers = await _repository.ListAsync(new ContactNumbersByBuyerSpec(buyerId), cancellationToken);
        return numbers.Count == 0 ? null : numbers[0];
    }

    public async Task<bool> IsRegisteredAsync(string buyerId, string canonicalNumber, CancellationToken cancellationToken = default)
    {
        var existing = await _repository.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndCanonicalSpec(buyerId, canonicalNumber),
            cancellationToken);
        return existing != null;
    }
}
