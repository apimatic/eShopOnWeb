using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly ITwilioLookupClient _lookupClient;

    public ContactNumberService(IRepository<ContactNumber> contactNumbers, ITwilioLookupClient lookupClient)
    {
        _contactNumbers = contactNumbers;
        _lookupClient = lookupClient;
    }

    public async Task<ContactNumber> RegisterAsync(string buyerId, string phoneNumber, string? countryCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw new ContactNumberRejectedException("A phone number is required.");
        }

        var lookup = await _lookupClient.LookupAsync(phoneNumber.Trim(), countryCode, cancellationToken);
        if (!SmsDestinationPolicy.IsUsableSmsDestination(lookup.LineType, lookup.LineTypeErrorCode, lookup.Valid)
            || string.IsNullOrWhiteSpace(lookup.CanonicalPhoneNumber))
        {
            var reason = lookup.Valid
                ? $"The provider does not consider this number a usable SMS destination (line type: {lookup.LineType ?? "unknown"})."
                : "The provider does not consider this number a usable destination.";
            throw new ContactNumberRejectedException(reason, lookup.ValidationErrors, lookup.LineType);
        }

        var existing = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndPhoneSpecification(buyerId, lookup.CanonicalPhoneNumber), cancellationToken);
        if (existing is not null)
        {
            throw new DuplicateException("This number is already registered.");
        }

        var contact = new ContactNumber(
            buyerId,
            lookup.CanonicalPhoneNumber,
            lookup.NationalFormat,
            lookup.CountryCode,
            lookup.LineType);

        return await _contactNumbers.AddAsync(contact, cancellationToken);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return numbers;
    }

    public async Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        var contact = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByIdAndBuyerSpecification(contactNumberId, buyerId), cancellationToken);
        if (contact is null)
        {
            throw new KeyNotFoundException("Contact number not found.");
        }

        await _contactNumbers.DeleteAsync(contact, cancellationToken);
    }

    public async Task<ContactNumber?> GetPreferredForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return numbers.FirstOrDefault();
    }

    public async Task<bool> IsStillRegisteredAsync(string buyerId, string canonicalPhoneNumber, CancellationToken cancellationToken = default)
    {
        var existing = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndPhoneSpecification(buyerId, canonicalPhoneNumber), cancellationToken);
        return existing is not null;
    }
}
