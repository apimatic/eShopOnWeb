using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly ISmsProvider _smsProvider;

    public ContactNumberService(IRepository<ContactNumber> contactNumbers, ISmsProvider smsProvider)
    {
        _contactNumbers = contactNumbers;
        _smsProvider = smsProvider;
    }

    public async Task<RegisterContactNumberResult> RegisterAsync(string buyerId, string rawNumber, CancellationToken ct = default)
    {
        // Reject a number the provider does not consider a usable destination here — at registration —
        // rather than at the moment a later message fails to go out. Store the provider's canonical form.
        var validation = await _smsProvider.ValidateNumberAsync(rawNumber, ct);
        if (!validation.IsValid || string.IsNullOrWhiteSpace(validation.CanonicalNumber))
        {
            return RegisterContactNumberResult.Rejected("The number is not a usable SMS destination.");
        }

        // A shopper registering the same number twice just gets the number they already have on file.
        var existing = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), ct);
        var duplicate = existing.FirstOrDefault(c => c.PhoneNumber == validation.CanonicalNumber);
        if (duplicate is not null)
        {
            return RegisterContactNumberResult.Ok(duplicate.Id);
        }

        var contactNumber = new ContactNumber(buyerId, validation.CanonicalNumber);
        contactNumber = await _contactNumbers.AddAsync(contactNumber, ct);
        return RegisterContactNumberResult.Ok(contactNumber.Id);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken ct = default)
    {
        return await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), ct);
    }

    public async Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken ct = default)
    {
        // Scoped to the caller: a number that is not theirs simply is not found.
        var contactNumber = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByIdForBuyerSpecification(contactNumberId, buyerId), ct);
        if (contactNumber is null)
        {
            return false;
        }

        await _contactNumbers.DeleteAsync(contactNumber, ct);
        return true;
    }
}
