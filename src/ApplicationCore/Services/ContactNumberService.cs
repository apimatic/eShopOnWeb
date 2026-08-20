using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
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

    public async Task<Result<ContactNumber>> RegisterAsync(
        string buyerId,
        string phoneNumber,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return Result<ContactNumber>.Invalid(new List<ValidationError>
            {
                new() { Identifier = "phoneNumber", ErrorMessage = "A phone number is required." }
            });
        }

        LookupNumberResult lookup;
        try
        {
            lookup = await _lookupClient.LookupAsync(phoneNumber.Trim(), cancellationToken);
        }
        catch (System.InvalidOperationException ex)
        {
            return Result<ContactNumber>.Error(ex.Message);
        }

        if (!lookup.Valid || string.IsNullOrWhiteSpace(lookup.CanonicalPhoneNumber))
        {
            var reasons = lookup.ValidationErrors.Count == 0
                ? new[] { "The provider does not consider this a usable destination." }
                : lookup.ValidationErrors.ToArray();

            return Result<ContactNumber>.Invalid(reasons
                .Select(reason => new ValidationError { Identifier = "phoneNumber", ErrorMessage = reason })
                .ToList());
        }

        var canonical = lookup.CanonicalPhoneNumber;
        var existing = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndPhoneSpec(buyerId, canonical), cancellationToken);
        if (existing != null)
        {
            throw new DuplicateException("This number is already registered.");
        }

        var contact = new ContactNumber(buyerId, canonical);
        await _contactNumbers.AddAsync(contact, cancellationToken);
        return Result<ContactNumber>.Success(contact);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListForBuyerAsync(
        string buyerId,
        CancellationToken cancellationToken = default)
    {
        var list = await _contactNumbers.ListAsync(new ContactNumbersByBuyerIdSpec(buyerId), cancellationToken);
        return list;
    }

    public async Task<Result> DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        var contact = await _contactNumbers.GetByIdAsync(contactNumberId, cancellationToken);
        if (contact == null || contact.BuyerId != buyerId)
        {
            return Result.NotFound();
        }

        await _contactNumbers.DeleteAsync(contact, cancellationToken);
        return Result.Success();
    }
}
