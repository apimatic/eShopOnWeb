using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ShopperContactNumber> _contactNumberRepository;
    private readonly ITwilioLookupClient _lookupClient;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ShopperContactNumber> contactNumberRepository,
        ITwilioLookupClient lookupClient,
        IAppLogger<ContactNumberService> logger)
    {
        _contactNumberRepository = contactNumberRepository;
        _lookupClient = lookupClient;
        _logger = logger;
    }

    public async Task<ShopperContactNumber> RegisterAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(phoneNumber, nameof(phoneNumber));

        PhoneNumberLookupResult lookup;
        try
        {
            lookup = await _lookupClient.LookupAsync(phoneNumber.Trim(), cancellationToken);
        }
        catch (TwilioApiException ex) when (ex.StatusCode is >= 400 and < 500 && ex.StatusCode is not 401 and not 403)
        {
            throw new InvalidContactNumberException();
        }

        if (!lookup.IsValid || string.IsNullOrWhiteSpace(lookup.CanonicalNumber))
        {
            throw new InvalidContactNumberException();
        }

        var canonical = lookup.CanonicalNumber;
        var existing = await _contactNumberRepository.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndCanonicalSpec(buyerId, canonical), cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var created = new ShopperContactNumber(buyerId, canonical);
        await _contactNumberRepository.AddAsync(created, cancellationToken);
        _logger.LogInformation("Registered a contact number for buyer {BuyerId}.", buyerId);
        return created;
    }

    public async Task<IReadOnlyList<ShopperContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var numbers = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerIdSpec(buyerId), cancellationToken);
        return numbers;
    }

    public async Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var existing = await _contactNumberRepository.FirstOrDefaultAsync(
            new ContactNumberByIdAndBuyerSpec(contactNumberId, buyerId), cancellationToken);
        if (existing is null)
        {
            return false;
        }

        await _contactNumberRepository.DeleteAsync(existing, cancellationToken);
        _logger.LogInformation("Removed contact number {ContactNumberId} for buyer {BuyerId}.", contactNumberId, buyerId);
        return true;
    }

    public async Task<string?> GetActiveDestinationAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var numbers = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerIdSpec(buyerId), cancellationToken);
        return numbers.FirstOrDefault()?.CanonicalNumber;
    }
}
