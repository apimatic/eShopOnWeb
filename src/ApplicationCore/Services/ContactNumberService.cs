using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
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

    public async Task<int> RegisterAsync(string buyerId, string rawNumber, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrWhiteSpace(rawNumber, nameof(rawNumber));

        // Reject an unusable destination here, at registration, rather than when a message later fails.
        var validation = await _smsProvider.ValidateNumberAsync(rawNumber, ct);
        if (!validation.IsUsable || string.IsNullOrEmpty(validation.CanonicalE164))
        {
            // Deliberately does not echo the offending number.
            throw new InvalidContactNumberException("The supplied number is not a usable messaging destination.");
        }

        // Store the provider's own canonical form, not whatever the caller typed.
        var contactNumber = new ContactNumber(buyerId, validation.CanonicalE164);
        await _contactNumbers.AddAsync(contactNumber, ct);
        return contactNumber.Id;
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), ct);
    }

    public async Task<bool> RemoveAsync(string buyerId, int contactNumberId, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        // Scoped to the owner so one shopper can never delete another's number.
        var existing = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndIdSpecification(buyerId, contactNumberId), ct);
        if (existing is null)
        {
            return false;
        }

        await _contactNumbers.DeleteAsync(existing, ct);
        return true;
    }
}
