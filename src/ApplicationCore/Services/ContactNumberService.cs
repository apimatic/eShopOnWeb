using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IPhoneNumberLookupService _lookup;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ContactNumber> contactNumbers,
        IPhoneNumberLookupService lookup,
        IAppLogger<ContactNumberService> logger)
    {
        _contactNumbers = contactNumbers;
        _lookup = lookup;
        _logger = logger;
    }

    public async Task<Result<ContactNumber>> RegisterAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            return Result<ContactNumber>.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return ResultFactory.Invalid<ContactNumber>(nameof(phoneNumber), "A phone number is required.");
        }

        PhoneNumberLookupResult lookup;
        try
        {
            lookup = await _lookup.LookupAsync(phoneNumber.Trim(), cancellationToken);
        }
        catch
        {
            _logger.LogWarning("Phone number lookup failed for buyer {BuyerId}.", buyerId);
            return Result<ContactNumber>.Error("The phone number could not be validated with the messaging provider.");
        }

        if (!lookup.IsValid || string.IsNullOrWhiteSpace(lookup.CanonicalNumber))
        {
            var detail = lookup.ValidationErrors.Count > 0
                ? string.Join(", ", lookup.ValidationErrors)
                : "not a usable destination";
            return ResultFactory.Invalid<ContactNumber>(
                nameof(phoneNumber),
                $"The messaging provider rejected this number ({detail}).");
        }

        var existing = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndPhoneSpecification(buyerId, lookup.CanonicalNumber),
            cancellationToken);
        if (existing is not null)
        {
            return Result<ContactNumber>.Success(existing);
        }

        var contact = new ContactNumber(buyerId, lookup.CanonicalNumber);
        await _contactNumbers.AddAsync(contact, cancellationToken);
        return Result<ContactNumber>.Success(contact);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            return new List<ContactNumber>();
        }

        return await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task<Result> DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            return Result.Unauthorized();
        }

        var contact = await _contactNumbers.GetByIdAsync(contactNumberId, cancellationToken);
        if (contact is null || contact.BuyerId != buyerId)
        {
            return Result.NotFound();
        }

        await _contactNumbers.DeleteAsync(contact, cancellationToken);
        return Result.Success();
    }
}
