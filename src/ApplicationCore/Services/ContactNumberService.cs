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

public class ContactNumberService : IContactNumberService
{
    private static readonly HashSet<string> UnusableLineTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "landline",
        "voicemail",
        "pager",
        "premium",
        "sharedCost",
        "uan",
        "tollFree"
    };

    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly ITwilioGateway _twilio;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ContactNumber> contactNumbers,
        ITwilioGateway twilio,
        IAppLogger<ContactNumberService> logger)
    {
        _contactNumbers = contactNumbers;
        _twilio = twilio;
        _logger = logger;
    }

    public async Task<ContactNumber> RegisterAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw new UnusablePhoneNumberException("A phone number is required.");
        }

        var lookup = await _twilio.LookupPhoneNumberAsync(phoneNumber.Trim(), cancellationToken);
        if (!lookup.IsValid || string.IsNullOrWhiteSpace(lookup.CanonicalPhoneNumber))
        {
            var reason = lookup.ValidationErrors.Count > 0
                ? string.Join(", ", lookup.ValidationErrors)
                : "the provider does not consider this a usable destination";
            throw new UnusablePhoneNumberException($"This number cannot be used: {reason}.");
        }

        if (!string.IsNullOrWhiteSpace(lookup.LineType)
            && lookup.LineTypeErrorCode is null
            && UnusableLineTypes.Contains(lookup.LineType))
        {
            throw new UnusablePhoneNumberException(
                $"This number cannot be used for SMS (line type '{lookup.LineType}').");
        }

        var canonical = lookup.CanonicalPhoneNumber;
        var existing = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndPhoneSpecification(buyerId, canonical), cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        var contact = new ContactNumber(buyerId, canonical, lookup.NationalFormat);
        await _contactNumbers.AddAsync(contact, cancellationToken);
        _logger.LogInformation("Registered contact number {ContactNumberId} for buyer {BuyerId}.", contact.Id, buyerId);
        return contact;
    }

    public async Task<IReadOnlyList<ContactNumber>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var list = await _contactNumbers.ListAsync(new ContactNumbersByBuyerIdSpecification(buyerId), cancellationToken);
        return list;
    }

    public async Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        var contact = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByIdAndBuyerSpecification(contactNumberId, buyerId), cancellationToken);
        if (contact == null)
        {
            return false;
        }

        await _contactNumbers.DeleteAsync(contact, cancellationToken);
        _logger.LogInformation("Removed contact number {ContactNumberId} for buyer {BuyerId}.", contactNumberId, buyerId);
        return true;
    }
}
