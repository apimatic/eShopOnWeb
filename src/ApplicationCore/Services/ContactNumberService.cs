using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly ITwilioMessagingClient _twilio;

    public ContactNumberService(IRepository<ContactNumber> contactNumbers, ITwilioMessagingClient twilio)
    {
        _contactNumbers = contactNumbers;
        _twilio = twilio;
    }

    public async Task<Result<ContactNumber>> RegisterAsync(string buyerId, string rawNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawNumber))
        {
            return Result<ContactNumber>.Invalid(new List<ValidationError> { new() { Identifier = "number", ErrorMessage = "A phone number is required." } });
        }

        // Validate up-front with the provider and canonicalize. An unusable destination is rejected
        // here, not at the moment a message later fails to go out.
        var lookup = await _twilio.LookupPhoneNumberAsync(rawNumber, cancellationToken);
        if (!lookup.Valid || string.IsNullOrEmpty(lookup.PhoneNumberE164))
        {
            return Result<ContactNumber>.Invalid(new List<ValidationError>
            {
                new() { Identifier = "number", ErrorMessage = "The phone number is not a usable SMS destination." }
            });
        }

        var canonical = lookup.PhoneNumberE164;

        // If the shopper already has this exact number on file, return the existing record instead of duplicating.
        var owned = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        var existing = owned.FirstOrDefault(c => c.PhoneNumber == canonical);
        if (existing is not null)
        {
            return Result<ContactNumber>.Success(existing);
        }

        var contactNumber = new ContactNumber(buyerId, canonical);
        await _contactNumbers.AddAsync(contactNumber, cancellationToken);
        return Result<ContactNumber>.Success(contactNumber);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var owned = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return owned;
    }

    public async Task<Result> DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        // Scope the lookup to the owner so one shopper can never delete another's number.
        var contactNumber = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByIdSpecification(contactNumberId, buyerId), cancellationToken);
        if (contactNumber is null)
        {
            return Result.NotFound();
        }

        await _contactNumbers.DeleteAsync(contactNumber, cancellationToken);
        return Result.Success();
    }
}
