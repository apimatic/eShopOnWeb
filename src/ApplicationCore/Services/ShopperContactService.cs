using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ShopperContactService : IShopperContactService
{
    private readonly ITwilioLookupClient _lookupClient;
    private readonly IRepository<ShopperContactNumber> _contactNumbers;
    private readonly IAppLogger<ShopperContactService> _logger;

    public ShopperContactService(
        ITwilioLookupClient lookupClient,
        IRepository<ShopperContactNumber> contactNumbers,
        IAppLogger<ShopperContactService> logger)
    {
        _lookupClient = lookupClient;
        _contactNumbers = contactNumbers;
        _logger = logger;
    }

    public async Task<ShopperContactNumber> RegisterAsync(string buyerId, string phoneNumber, string? countryCode, CancellationToken cancellationToken = default)
    {
        var lookup = await _lookupClient.LookupAsync(phoneNumber, countryCode, cancellationToken);
        if (!lookup.IsUsableSmsDestination())
        {
            var reason = lookup.ValidationErrors.Count > 0
                ? string.Join(", ", lookup.ValidationErrors)
                : lookup.LineType ?? "not a usable SMS destination";
            throw new UnusableContactNumberException($"This number cannot be used as an SMS destination ({reason}).");
        }

        var canonical = lookup.PhoneNumber!;
        var existing = await _contactNumbers.FirstOrDefaultAsync(new ShopperContactNumberByCanonicalSpec(buyerId, canonical), cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        var registered = await _contactNumbers.AddAsync(new ShopperContactNumber(buyerId, canonical, lookup.NationalFormat), cancellationToken);
        _logger.LogInformation("Registered contact number {ContactNumberId} for buyer {BuyerId}.", registered.Id, buyerId);
        return registered;
    }

    public async Task<IReadOnlyList<ShopperContactNumber>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var numbers = await _contactNumbers.ListAsync(new ShopperContactNumbersByBuyerIdSpec(buyerId), cancellationToken);
        return numbers;
    }

    public async Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        var number = await _contactNumbers.GetByIdAsync(contactNumberId, cancellationToken);
        if (number == null || number.BuyerId != buyerId)
        {
            throw new KeyNotFoundException("Contact number was not found.");
        }

        await _contactNumbers.DeleteAsync(number, cancellationToken);
        _logger.LogInformation("Removed contact number {ContactNumberId} for buyer {BuyerId}.", contactNumberId, buyerId);
    }
}
