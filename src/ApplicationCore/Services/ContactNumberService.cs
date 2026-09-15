using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ShopperContactNumber> _contactNumbers;
    private readonly ISmsNotificationClient _sms;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ShopperContactNumber> contactNumbers,
        ISmsNotificationClient sms,
        IAppLogger<ContactNumberService> logger)
    {
        _contactNumbers = contactNumbers;
        _sms = sms;
        _logger = logger;
    }

    public async Task<ShopperContactNumber> RegisterAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(phoneNumber, nameof(phoneNumber));

        PhoneNumberLookupResult lookup;
        try
        {
            lookup = await _sms.LookupAsync(phoneNumber.Trim(), cancellationToken);
        }
        catch (SmsProviderException ex) when ((int?)ex.StatusCode is >= 400 and < 500 and not 401 and not 403)
        {
            throw new ContactNumberRejectedException("The messaging provider does not consider this a usable destination.");
        }

        if (!lookup.IsValid || string.IsNullOrWhiteSpace(lookup.CanonicalNumber))
        {
            throw new ContactNumberRejectedException("The messaging provider does not consider this a usable destination.");
        }

        var existing = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndCanonicalSpec(buyerId, lookup.CanonicalNumber), cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var created = new ShopperContactNumber(buyerId, lookup.CanonicalNumber);
        await _contactNumbers.AddAsync(created, cancellationToken);
        _logger.LogInformation("Registered a contact number for buyer {BuyerId}.", buyerId);
        return created;
    }

    public async Task<IReadOnlyList<ShopperContactNumber>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpec(buyerId), cancellationToken);
        return numbers;
    }

    public async Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var existing = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByIdAndBuyerSpec(contactNumberId, buyerId), cancellationToken);
        if (existing is null)
        {
            throw new EntityNotFoundException("Contact number was not found.");
        }

        await _contactNumbers.DeleteAsync(existing, cancellationToken);
        _logger.LogInformation("Removed a contact number for buyer {BuyerId}.", buyerId);
    }

    public async Task<ShopperContactNumber?> GetPrimaryAsync(string buyerId, CancellationToken cancellationToken)
    {
        var numbers = await ListForBuyerAsync(buyerId, cancellationToken);
        return numbers.FirstOrDefault();
    }
}
