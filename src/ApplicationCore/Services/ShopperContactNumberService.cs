using System;
using System.Collections.Generic;
using System.Linq;
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
    private readonly ISmsNotificationGateway _gateway;

    public ShopperContactNumberService(
        IRepository<ShopperContactNumber> repository,
        ISmsNotificationGateway gateway)
    {
        _repository = repository;
        _gateway = gateway;
    }

    public async Task<ShopperContactNumber> RegisterAsync(
        string buyerId,
        string phoneNumber,
        string? countryCode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
            throw new UnusableContactNumberException("A mobile number is required.");

        PhoneLookupResult lookup;
        try
        {
            lookup = await _gateway.LookupDestinationAsync(phoneNumber.Trim(), countryCode, cancellationToken);
        }
        catch (NotificationProviderException)
        {
            throw;
        }

        if (!lookup.IsUsable || string.IsNullOrWhiteSpace(lookup.CanonicalNumber))
            throw new UnusableContactNumberException(lookup.RejectionReason ?? "This number is not a usable destination.");

        var existing = await _repository.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndCanonicalSpecification(buyerId, lookup.CanonicalNumber),
            cancellationToken);
        if (existing is not null)
            throw new DuplicateException("That mobile number is already registered.");

        var entity = new ShopperContactNumber(buyerId, lookup.CanonicalNumber);
        return await _repository.AddAsync(entity, cancellationToken);
    }

    public async Task<IReadOnlyList<ShopperContactNumber>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken)
    {
        var items = await _repository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return items;
    }

    public async Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken)
    {
        var entity = await _repository.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndIdSpecification(buyerId, contactNumberId),
            cancellationToken);
        if (entity is null)
            throw new ContactNumberNotFoundException();

        await _repository.DeleteAsync(entity, cancellationToken);
    }
}
