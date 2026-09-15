using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IPhoneNumberLookup _lookup;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ContactNumber> contactNumbers,
        IPhoneNumberLookup lookup,
        IAppLogger<ContactNumberService> logger)
    {
        _contactNumbers = contactNumbers;
        _lookup = lookup;
        _logger = logger;
    }

    public async Task<Result<ContactNumber>> RegisterAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return ResultHelpers.Invalid<ContactNumber>("phoneNumber", "A phone number is required.");
        }

        PhoneNumberLookupResult lookup;
        try
        {
            lookup = await _lookup.LookupAsync(phoneNumber.Trim(), cancellationToken);
        }
        catch (System.Exception ex)
        {
            _logger.LogWarning("Phone number lookup failed: {Message}", ex.Message);
            return Result<ContactNumber>.Error("The number could not be validated with the messaging provider.");
        }

        if (!lookup.IsValidDestination || string.IsNullOrEmpty(lookup.CanonicalNumber))
        {
            return ResultHelpers.Invalid<ContactNumber>("phoneNumber", "The number is not a usable destination.");
        }

        var existing = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndCanonicalSpec(buyerId, lookup.CanonicalNumber),
            cancellationToken);
        if (existing != null)
        {
            return Result<ContactNumber>.Success(existing);
        }

        var contact = new ContactNumber(buyerId, lookup.CanonicalNumber);
        await _contactNumbers.AddAsync(contact, cancellationToken);
        return Result<ContactNumber>.Success(contact);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpec(buyerId), cancellationToken);
    }

    public async Task<Result> DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        var contact = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndIdSpec(buyerId, contactNumberId),
            cancellationToken);
        if (contact == null)
        {
            return Result.NotFound();
        }

        await _contactNumbers.DeleteAsync(contact, cancellationToken);
        return Result.Success();
    }
}
