using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly ISmsGateway _smsGateway;

    public ContactNumberService(IRepository<ContactNumber> contactNumbers, ISmsGateway smsGateway)
    {
        _contactNumbers = contactNumbers;
        _smsGateway = smsGateway;
    }

    public async Task<ContactNumberRegistrationResult> RegisterAsync(string buyerId, string rawPhoneNumber, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawPhoneNumber))
            return new ContactNumberRegistrationResult(false, "A phone number is required.", null);

        // Reject an unusable destination here, at registration — not at the moment a message fails.
        var validation = await _smsGateway.ValidateNumberAsync(rawPhoneNumber, ct);
        if (!validation.IsValid || string.IsNullOrWhiteSpace(validation.CanonicalNumber))
            return new ContactNumberRegistrationResult(false, "The number is not a usable SMS destination.", null);

        var canonical = validation.CanonicalNumber!;

        // Store the provider's canonical form, and keep registration idempotent per shopper.
        var existing = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), ct);
        var already = existing.FirstOrDefault(c => c.PhoneNumber == canonical);
        if (already is not null)
            return new ContactNumberRegistrationResult(true, null, already);

        var contactNumber = new ContactNumber(buyerId, canonical);
        contactNumber = await _contactNumbers.AddAsync(contactNumber, ct);
        return new ContactNumberRegistrationResult(true, null, contactNumber);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken ct = default)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), ct);
        return numbers.ToList();
    }

    public async Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken ct = default)
    {
        // Ownership is part of the query: another shopper's number simply is not found here.
        var number = await _contactNumbers.FirstOrDefaultAsync(new ContactNumberByIdSpecification(contactNumberId, buyerId), ct);
        if (number is null)
            return false;

        await _contactNumbers.DeleteAsync(number, ct);
        return true;
    }
}
