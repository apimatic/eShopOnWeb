using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _repository;
    private readonly IMessagingProvider _messagingProvider;

    public ContactNumberService(IRepository<ContactNumber> repository, IMessagingProvider messagingProvider)
    {
        _repository = repository;
        _messagingProvider = messagingProvider;
    }

    public async Task<ContactNumberRegistrationResult> RegisterAsync(string buyerId, string rawNumber, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        if (string.IsNullOrWhiteSpace(rawNumber))
        {
            return ContactNumberRegistrationResult.Rejected("A phone number is required.");
        }

        // Ask the provider whether this is a usable destination and, if so, for its canonical form.
        // A provider-unreachable failure surfaces as MessagingProviderException (mapped at the boundary),
        // so a valid number is never silently rejected because the provider was momentarily unavailable.
        var validation = await _messagingProvider.ValidateNumberAsync(rawNumber.Trim(), cancellationToken);
        if (!validation.IsValid || string.IsNullOrEmpty(validation.CanonicalE164))
        {
            return ContactNumberRegistrationResult.Rejected(validation.Reason ?? "The phone number is not a usable SMS destination.");
        }

        var canonical = validation.CanonicalE164;

        // Registering the same number twice is a no-op that returns the existing registration.
        var existing = await _repository.FirstOrDefaultAsync(new ContactNumberByValueForBuyerSpecification(buyerId, canonical), cancellationToken);
        if (existing is not null)
        {
            return ContactNumberRegistrationResult.Ok(existing);
        }

        var contactNumber = new ContactNumber(buyerId, canonical);
        await _repository.AddAsync(contactNumber, cancellationToken);
        return ContactNumberRegistrationResult.Ok(contactNumber);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _repository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task<bool> RemoveAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var contactNumber = await _repository.FirstOrDefaultAsync(new ContactNumberByIdForBuyerSpecification(contactNumberId, buyerId), cancellationToken);
        if (contactNumber is null)
        {
            return false;
        }

        await _repository.DeleteAsync(contactNumber, cancellationToken);
        return true;
    }
}
