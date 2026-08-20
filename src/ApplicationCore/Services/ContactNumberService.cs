using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ShopperContactNumber> _repository;
    private readonly ISmsNotificationGateway _gateway;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ShopperContactNumber> repository,
        ISmsNotificationGateway gateway,
        IAppLogger<ContactNumberService> logger)
    {
        _repository = repository;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<ShopperContactNumber> RegisterAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(phoneNumber, nameof(phoneNumber));

        PhoneLookupResult lookup;
        try
        {
            lookup = await _gateway.LookupAsync(phoneNumber, cancellationToken);
        }
        catch (SmsProviderException ex)
        {
            _logger.LogWarning("Contact number lookup failed with provider status {Status}", ex.StatusCode?.ToString() ?? "none");
            throw new UnusableContactNumberException("The number could not be verified as a usable destination.");
        }

        if (!lookup.IsUsable || string.IsNullOrWhiteSpace(lookup.CanonicalNumber))
        {
            throw new UnusableContactNumberException(
                lookup.RejectionReason ?? "The number is not a usable destination.");
        }

        var existing = await _repository.ListAsync(new ShopperContactNumbersByBuyerSpec(buyerId), cancellationToken);
        var duplicate = existing.FirstOrDefault(n => n.CanonicalNumber == lookup.CanonicalNumber);
        if (duplicate != null)
        {
            return duplicate;
        }

        var contact = new ShopperContactNumber(buyerId, lookup.CanonicalNumber);
        return await _repository.AddAsync(contact, cancellationToken);
    }

    public async Task<IReadOnlyList<ShopperContactNumber>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var numbers = await _repository.ListAsync(new ShopperContactNumbersByBuyerSpec(buyerId), cancellationToken);
        return numbers;
    }

    public async Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var contact = await _repository.FirstOrDefaultAsync(
            new ShopperContactNumberByIdSpec(contactNumberId, buyerId), cancellationToken);
        if (contact is null)
        {
            throw new ContactNumberNotFoundException(contactNumberId);
        }

        await _repository.DeleteAsync(contact, cancellationToken);
    }
}
