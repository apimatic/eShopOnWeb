using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _repository;
    private readonly ISmsSender _smsSender;

    public ContactNumberService(IRepository<ContactNumber> repository, ISmsSender smsSender)
    {
        _repository = repository;
        _smsSender = smsSender;
    }

    public async Task<RegisterContactNumberResult> RegisterAsync(string buyerId, string rawNumber, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        if (string.IsNullOrWhiteSpace(rawNumber))
        {
            return RegisterContactNumberResult.Rejected("A phone number is required.");
        }

        // Reject a number the provider does not consider a usable destination here, up front —
        // not at the moment a message later fails to go out.
        var lookup = await _smsSender.LookupAsync(rawNumber, cancellationToken);
        if (!lookup.IsValid || string.IsNullOrEmpty(lookup.CanonicalE164))
        {
            return RegisterContactNumberResult.Rejected("The number is not a usable SMS destination.");
        }

        var canonical = lookup.CanonicalE164;

        // Store the provider's canonical form. If the shopper already has this number, keep the
        // registration idempotent rather than creating a duplicate.
        var existing = await _repository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        var already = existing.FirstOrDefault(c => c.PhoneNumber == canonical);
        if (already != null)
        {
            return RegisterContactNumberResult.Registered(already);
        }

        var contactNumber = new ContactNumber(buyerId, canonical);
        await _repository.AddAsync(contactNumber, cancellationToken);
        return RegisterContactNumberResult.Registered(contactNumber);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _repository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        // Scope by buyer so a shopper can only ever delete their own number.
        var contactNumber = await _repository.FirstOrDefaultAsync(
            new ContactNumberByIdForBuyerSpecification(contactNumberId, buyerId), cancellationToken);
        if (contactNumber == null)
        {
            return false;
        }

        await _repository.DeleteAsync(contactNumber, cancellationToken);
        return true;
    }
}
