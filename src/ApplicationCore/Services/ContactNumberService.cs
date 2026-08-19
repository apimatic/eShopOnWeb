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
    private readonly IRepository<ContactNumber> _repository;
    private readonly ISmsProvider _smsProvider;

    public ContactNumberService(IRepository<ContactNumber> repository, ISmsProvider smsProvider)
    {
        _repository = repository;
        _smsProvider = smsProvider;
    }

    public async Task<ContactNumberRegistrationResult> RegisterAsync(
        string ownerId, string rawPhoneNumber, string? defaultCountryCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawPhoneNumber))
            return ContactNumberRegistrationResult.Rejected(new[] { "A phone number is required." });

        // Validate & canonicalise at the edge; reject an unusable destination up front.
        var validation = await _smsProvider.ValidateAndCanonicalizeAsync(rawPhoneNumber, defaultCountryCode, cancellationToken);
        if (!validation.IsValid || string.IsNullOrEmpty(validation.CanonicalE164))
            return ContactNumberRegistrationResult.Rejected(validation.ValidationErrors);

        var canonical = validation.CanonicalE164!;

        // Idempotent registration: if the shopper already has this canonical number, return it.
        var existing = await _repository.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), cancellationToken);
        var alreadyRegistered = existing.FirstOrDefault(c => c.PhoneNumber == canonical);
        if (alreadyRegistered != null)
            return ContactNumberRegistrationResult.Ok(alreadyRegistered);

        var contactNumber = new ContactNumber(ownerId, canonical);
        await _repository.AddAsync(contactNumber, cancellationToken);
        return ContactNumberRegistrationResult.Ok(contactNumber);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        return await _repository.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), cancellationToken);
    }

    public async Task<bool> DeleteAsync(string ownerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        // Scoped by owner so one shopper can never delete another's number.
        var toDelete = await _repository.FirstOrDefaultAsync(
            new ContactNumberByIdAndOwnerSpecification(contactNumberId, ownerId), cancellationToken);
        if (toDelete is null)
            return false;

        await _repository.DeleteAsync(toDelete, cancellationToken);
        return true;
    }
}
