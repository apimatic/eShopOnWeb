using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
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

    public async Task<ContactNumber> RegisterAsync(string buyerId, string rawPhoneNumber, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(rawPhoneNumber, nameof(rawPhoneNumber));

        var lookup = await _lookupClient.LookupAsync(rawPhoneNumber.Trim(), cancellationToken);
        if (!lookup.IsValid || string.IsNullOrWhiteSpace(lookup.CanonicalPhoneNumber))
        {
            var reason = lookup.ValidationErrors.Count > 0
                ? string.Join(", ", lookup.ValidationErrors)
                : "the provider does not consider this a usable destination";
            throw new UnusablePhoneNumberException($"The phone number is not a usable destination: {reason}.");
        }

        var existing = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndCanonicalSpec(buyerId, lookup.CanonicalPhoneNumber),
            cancellationToken);

        if (existing is not null)
        {
            throw new ContactNumberAlreadyRegisteredException(existing);
        }

        var contactNumber = new ContactNumber(buyerId, lookup.CanonicalPhoneNumber);
        return await _contactNumbers.AddAsync(contactNumber, cancellationToken);
    }

    public async Task<System.Collections.Generic.IReadOnlyList<ContactNumber>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerIdSpec(buyerId), cancellationToken);
        return numbers;
    }

    public async Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var contactNumber = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByIdAndBuyerSpec(contactNumberId, buyerId),
            cancellationToken);

        if (contactNumber is null)
        {
            return false;
        }

        await _contactNumbers.DeleteAsync(contactNumber, cancellationToken);
        return true;
    }
}
