using System.Collections.Generic;
using System.Linq;
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
    private readonly ISmsNotificationService _sms;

    public ContactNumberService(IRepository<ContactNumber> repository, ISmsNotificationService sms)
    {
        _repository = repository;
        _sms = sms;
    }

    public async Task<ContactNumberRegistration> RegisterAsync(string ownerId, string rawNumber, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));

        if (string.IsNullOrWhiteSpace(rawNumber))
            return new ContactNumberRegistration(false, null, "A phone number is required.");

        // Reject an unusable destination here (may throw SmsNotificationException on a provider failure).
        var validation = await _sms.ValidatePhoneNumberAsync(rawNumber, cancellationToken);
        if (!validation.IsValid || string.IsNullOrEmpty(validation.CanonicalNumber))
            return new ContactNumberRegistration(false, null, validation.Reason ?? "The number is not a valid destination.");

        // Store the provider's canonical form. If the shopper already has it on file, keep a single row.
        var existing = await _repository.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), cancellationToken);
        var already = existing.FirstOrDefault(c => c.PhoneNumber == validation.CanonicalNumber);
        if (already is not null)
            return new ContactNumberRegistration(true, already, null);

        var contactNumber = new ContactNumber(ownerId, validation.CanonicalNumber!);
        await _repository.AddAsync(contactNumber, cancellationToken);
        return new ContactNumberRegistration(true, contactNumber, null);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        return await _repository.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), cancellationToken);
    }

    public async Task<bool> DeleteAsync(string ownerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));

        var contactNumber = await _repository.FirstOrDefaultAsync(
            new ContactNumberByOwnerAndIdSpecification(ownerId, contactNumberId), cancellationToken);
        if (contactNumber is null)
            return false;

        await _repository.DeleteAsync(contactNumber, cancellationToken);
        return true;
    }
}
