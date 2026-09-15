using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class ShopperContactNumberService : IShopperContactNumberService
{
    private readonly ITwilioLookupClient _lookupClient;
    private readonly IRepository<ShopperContactNumber> _repository;

    public ShopperContactNumberService(
        ITwilioLookupClient lookupClient,
        IRepository<ShopperContactNumber> repository)
    {
        _lookupClient = lookupClient;
        _repository = repository;
    }

    public async Task<ShopperContactNumber> RegisterAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw new InvalidContactNumberException();
        }

        var lookup = await _lookupClient.LookupAsync(phoneNumber.Trim(), cancellationToken);
        if (!lookup.IsValid || string.IsNullOrWhiteSpace(lookup.CanonicalNumber))
        {
            throw new InvalidContactNumberException();
        }

        var existing = await _repository.FirstOrDefaultAsync(
            new ShopperContactNumberByCanonicalSpecification(buyerId, lookup.CanonicalNumber),
            cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var created = new ShopperContactNumber(buyerId, lookup.CanonicalNumber);
        return await _repository.AddAsync(created, cancellationToken);
    }

    public async Task<IReadOnlyList<ShopperContactNumber>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var items = await _repository.ListAsync(new ShopperContactNumbersSpecification(buyerId), cancellationToken);
        return items;
    }

    public async Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        var existing = await _repository.GetByIdAsync(contactNumberId, cancellationToken);
        if (existing is null || existing.BuyerId != buyerId)
        {
            throw new KeyNotFoundException("Contact number was not found.");
        }

        await _repository.DeleteAsync(existing, cancellationToken);
    }
}
