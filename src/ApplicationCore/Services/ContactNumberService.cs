using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly ITwilioMessagingClient _twilio;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        ITwilioMessagingClient twilio,
        IRepository<ContactNumber> contactNumbers,
        IAppLogger<ContactNumberService> logger)
    {
        _twilio = twilio;
        _contactNumbers = contactNumbers;
        _logger = logger;
    }

    public async Task<ContactNumber> RegisterAsync(string buyerId, string rawPhoneNumber, string? countryCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawPhoneNumber))
        {
            throw new InvalidContactNumberException("A phone number is required.");
        }

        var lookup = await _twilio.LookupAsync(rawPhoneNumber.Trim(), countryCode, cancellationToken);
        if (!lookup.IsUsable || string.IsNullOrWhiteSpace(lookup.CanonicalPhoneNumber))
        {
            var reasons = lookup.ValidationErrors.Count == 0
                ? "the provider does not consider it a usable destination"
                : string.Join(", ", lookup.ValidationErrors);
            throw new InvalidContactNumberException($"The phone number is not a usable destination ({reasons}).");
        }

        var existing = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndPhoneSpec(buyerId, lookup.CanonicalPhoneNumber), cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        var contact = new ContactNumber(buyerId, lookup.CanonicalPhoneNumber, lookup.NationalFormat, lookup.CountryCode);
        await _contactNumbers.AddAsync(contact, cancellationToken);
        _logger.LogInformation("Registered a contact number for buyer {BuyerId} with id {ContactNumberId}.", buyerId, contact.Id);
        return contact;
    }

    public async Task<IReadOnlyList<ContactNumber>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpec(buyerId), cancellationToken);
        return numbers;
    }

    public async Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        var contact = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndIdSpec(buyerId, contactNumberId), cancellationToken);
        if (contact == null)
        {
            return false;
        }

        await _contactNumbers.DeleteAsync(contact, cancellationToken);
        _logger.LogInformation("Removed contact number {ContactNumberId} for buyer {BuyerId}.", contactNumberId, buyerId);
        return true;
    }
}
