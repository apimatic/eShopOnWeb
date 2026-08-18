using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly ISmsGateway _smsGateway;

    public ContactNumberService(IRepository<ContactNumber> contactNumberRepository, ISmsGateway smsGateway)
    {
        _contactNumberRepository = contactNumberRepository;
        _smsGateway = smsGateway;
    }

    public async Task<ContactNumberRegistration> RegisterAsync(string ownerId, string rawNumber, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));

        if (string.IsNullOrWhiteSpace(rawNumber))
        {
            return new ContactNumberRegistration(false, null, "A phone number is required.");
        }

        // Validate + canonicalize with the provider up front, so an unusable number is rejected here rather
        // than at the moment a message fails to go out. A provider outage surfaces as SmsGatewayException.
        var validation = await _smsGateway.ValidateNumberAsync(rawNumber, ct);
        if (!validation.IsValid || string.IsNullOrEmpty(validation.CanonicalE164))
        {
            return new ContactNumberRegistration(false, null, "The phone number is not a usable destination.");
        }

        var canonical = validation.CanonicalE164;

        // Store the provider's canonical form. If the shopper already has this number, keep it idempotent.
        var existing = await _contactNumberRepository.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), ct);
        var already = existing.FirstOrDefault(c => c.E164Number == canonical);
        if (already is not null)
        {
            return new ContactNumberRegistration(true, already, null);
        }

        var contactNumber = new ContactNumber(ownerId, canonical);
        await _contactNumberRepository.AddAsync(contactNumber, ct);
        return new ContactNumberRegistration(true, contactNumber, null);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string ownerId, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        return await _contactNumberRepository.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), ct);
    }

    public async Task<bool> DeleteAsync(string ownerId, int contactNumberId, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));

        // Scope the lookup to the owner: a shopper can only delete their own number.
        var spec = new ContactNumberByIdForOwnerSpecification(contactNumberId, ownerId);
        var contactNumber = await _contactNumberRepository.FirstOrDefaultAsync(spec, ct);
        if (contactNumber is null)
        {
            return false;
        }

        await _contactNumberRepository.DeleteAsync(contactNumber, ct);
        return true;
    }
}
