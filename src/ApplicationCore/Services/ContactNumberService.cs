using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly ISmsProvider _smsProvider;

    public ContactNumberService(IRepository<ContactNumber> contactNumberRepository, ISmsProvider smsProvider)
    {
        _contactNumberRepository = contactNumberRepository;
        _smsProvider = smsProvider;
    }

    public async Task<ContactNumberRegistrationResult> RegisterAsync(string buyerId, string rawNumber, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        if (string.IsNullOrWhiteSpace(rawNumber))
            return ContactNumberRegistrationResult.Rejected(new[] { "NUMBER_REQUIRED" });

        // Reject an unusable destination up front (at registration), not later when a message fails.
        var validation = await _smsProvider.ValidateAsync(rawNumber, cancellationToken);
        if (!validation.IsValid || string.IsNullOrEmpty(validation.CanonicalE164))
            return ContactNumberRegistrationResult.Rejected(validation.Errors);

        // Store the provider's canonical form; if the shopper already has it, keep it idempotent.
        var existing = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        var already = existing.FirstOrDefault(c => c.E164Number == validation.CanonicalE164);
        if (already != null)
            return ContactNumberRegistrationResult.Registered(already);

        var contactNumber = new ContactNumber(buyerId, validation.CanonicalE164);
        await _contactNumberRepository.AddAsync(contactNumber, cancellationToken);
        return ContactNumberRegistrationResult.Registered(contactNumber);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var numbers = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return numbers;
    }

    public async Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        // Scoped by owner: a number that belongs to another shopper is simply not found here.
        var contactNumber = (await _contactNumberRepository.ListAsync(
            new ContactNumberByIdSpecification(buyerId, contactNumberId), cancellationToken)).FirstOrDefault();

        if (contactNumber == null)
            return false;

        await _contactNumberRepository.DeleteAsync(contactNumber, cancellationToken);
        return true;
    }
}
