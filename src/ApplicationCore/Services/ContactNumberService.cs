using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NotFoundException = Microsoft.eShopWeb.ApplicationCore.Exceptions.NotFoundException;

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

    public async Task<ContactNumber> RegisterAsync(string buyerId, string rawPhoneNumber, CancellationToken ct = default)
    {
        Guard.Against.NullOrWhiteSpace(buyerId, nameof(buyerId));
        Guard.Against.NullOrWhiteSpace(rawPhoneNumber, nameof(rawPhoneNumber));

        // Reject an unusable destination here, up front — not when a message later fails to go out —
        // and store the provider's own canonical form, not whatever the caller typed.
        var validation = await _smsGateway.ValidateNumberAsync(rawPhoneNumber, ct);
        if (!validation.IsValid || string.IsNullOrWhiteSpace(validation.CanonicalE164))
        {
            throw new InvalidPhoneNumberException(
                "The phone number is not a valid, reachable mobile destination and cannot be registered.");
        }

        var canonical = validation.CanonicalE164!;

        // Avoid registering the same number twice for one shopper (which would double their messages).
        var existing = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), ct);
        var already = existing.FirstOrDefault(c => c.PhoneNumber == canonical);
        if (already is not null)
        {
            return already;
        }

        var contactNumber = new ContactNumber(buyerId, canonical);
        return await _contactNumberRepository.AddAsync(contactNumber, ct);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken ct = default)
    {
        Guard.Against.NullOrWhiteSpace(buyerId, nameof(buyerId));
        return await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), ct);
    }

    public async Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken ct = default)
    {
        Guard.Against.NullOrWhiteSpace(buyerId, nameof(buyerId));

        // Scoped lookup: a shopper can only ever delete their own number.
        var contactNumber = await _contactNumberRepository
            .FirstOrDefaultAsync(new ContactNumberByIdForBuyerSpecification(contactNumberId, buyerId), ct);
        if (contactNumber is null)
        {
            throw new NotFoundException($"Contact number {contactNumberId} was not found.");
        }

        await _contactNumberRepository.DeleteAsync(contactNumber, ct);
    }
}
