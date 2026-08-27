using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ShopperContactNumberService : IShopperContactNumberService
{
    private readonly IRepository<ShopperContactNumber> _contactNumbers;
    private readonly ITwilioMessagingClient _twilio;
    private readonly IAppLogger<ShopperContactNumberService> _logger;

    public ShopperContactNumberService(
        IRepository<ShopperContactNumber> contactNumbers,
        ITwilioMessagingClient twilio,
        IAppLogger<ShopperContactNumberService> logger)
    {
        _contactNumbers = contactNumbers;
        _twilio = twilio;
        _logger = logger;
    }

    public async Task<ShopperContactNumber> RegisterAsync(string buyerId, string rawPhoneNumber)
    {
        if (string.IsNullOrWhiteSpace(rawPhoneNumber))
        {
            throw new InvalidContactNumberException("A phone number is required.");
        }

        var lookup = await _twilio.LookupPhoneNumberAsync(rawPhoneNumber.Trim());
        if (!lookup.Valid || string.IsNullOrWhiteSpace(lookup.CanonicalPhoneNumber))
        {
            throw new InvalidContactNumberException("The provider does not consider this number a usable destination.");
        }

        var canonical = lookup.CanonicalPhoneNumber;
        var existingForBuyer = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndCanonicalNumberSpecification(buyerId, canonical));
        if (existingForBuyer != null)
        {
            throw new DuplicateException("This contact number is already registered.");
        }

        var existingAnywhere = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByCanonicalNumberSpecification(canonical));
        if (existingAnywhere != null)
        {
            throw new DuplicateException("This contact number is already registered.");
        }

        var contactNumber = new ShopperContactNumber(buyerId, canonical);
        await _contactNumbers.AddAsync(contactNumber);
        _logger.LogInformation("Registered contact number {ContactNumberId} for buyer", contactNumber.Id);
        return contactNumber;
    }

    public async Task<IReadOnlyList<ShopperContactNumber>> ListForBuyerAsync(string buyerId)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerIdSpecification(buyerId));
        return numbers;
    }

    public async Task DeleteAsync(string buyerId, int contactNumberId)
    {
        var contactNumber = await _contactNumbers.FirstOrDefaultAsync(new ContactNumberByIdSpecification(contactNumberId));
        if (contactNumber == null || contactNumber.BuyerId != buyerId)
        {
            throw new ContactNumberNotFoundException(contactNumberId);
        }

        await _contactNumbers.DeleteAsync(contactNumber);
        _logger.LogInformation("Removed contact number {ContactNumberId} for buyer", contactNumberId);
    }
}
