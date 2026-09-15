using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private static readonly HashSet<string> UnusableLineTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "landline", "voicemail", "pager", "tollFree", "premium", "sharedCost", "uan"
    };

    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly ISmsProvider _smsProvider;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ContactNumber> contactNumbers,
        ISmsProvider smsProvider,
        IAppLogger<ContactNumberService> logger)
    {
        _contactNumbers = contactNumbers;
        _smsProvider = smsProvider;
        _logger = logger;
    }

    public async Task<Result<ContactNumber>> RegisterAsync(string buyerId, string rawNumber, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (string.IsNullOrWhiteSpace(rawNumber))
        {
            return AppResults.Invalid<ContactNumber>("phoneNumber", "A phone number is required.");
        }

        PhoneLookupResult lookup;
        try
        {
            lookup = await _smsProvider.LookupAsync(rawNumber, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Phone number lookup failed: {Message}", ex.Message);
            return Result<ContactNumber>.Error("The number could not be validated with the messaging provider.");
        }

        if (!lookup.Valid || string.IsNullOrEmpty(lookup.CanonicalPhoneNumber))
        {
            var reason = lookup.ValidationErrors.Count > 0
                ? string.Join(", ", lookup.ValidationErrors)
                : "NOT_A_USABLE_DESTINATION";
            return AppResults.Invalid<ContactNumber>("phoneNumber", $"This number is not a usable destination ({reason}).");
        }

        if (!string.IsNullOrEmpty(lookup.LineType)
            && lookup.LineTypeErrorCode is null
            && UnusableLineTypes.Contains(lookup.LineType))
        {
            return AppResults.Invalid<ContactNumber>("phoneNumber", "This number is not a usable mobile destination.");
        }

        var existing = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndPhoneSpecification(buyerId, lookup.CanonicalPhoneNumber),
            cancellationToken);
        if (existing != null)
        {
            throw new DuplicateException("This number is already registered.");
        }

        var contact = new ContactNumber(
            buyerId,
            lookup.CanonicalPhoneNumber,
            lookup.NationalFormat,
            lookup.CountryCode,
            lookup.LineType);

        await _contactNumbers.AddAsync(contact, cancellationToken);
        _logger.LogInformation("Registered a contact number for buyer {BuyerId} as {ContactNumberId}.", buyerId, contact.Id);
        return Result<ContactNumber>.Success(contact);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var items = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return items;
    }

    public async Task<Result> DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var contact = await _contactNumbers.GetByIdAsync(contactNumberId, cancellationToken);
        if (contact == null || contact.BuyerId != buyerId)
        {
            return Result.NotFound();
        }

        await _contactNumbers.DeleteAsync(contact, cancellationToken);
        _logger.LogInformation("Removed contact number {ContactNumberId} for buyer {BuyerId}.", contactNumberId, buyerId);
        return Result.Success();
    }
}
