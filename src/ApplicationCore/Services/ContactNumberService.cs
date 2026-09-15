using System.Collections.Generic;
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
    private readonly ISmsSender _smsSender;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ContactNumber> contactNumbers,
        ISmsSender smsSender,
        IAppLogger<ContactNumberService> logger)
    {
        _contactNumbers = contactNumbers;
        _smsSender = smsSender;
        _logger = logger;
    }

    public async Task<ContactNumber> RegisterAsync(string ownerId, string rawPhoneNumber, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(rawPhoneNumber, nameof(rawPhoneNumber));

        // Reject a number the provider does not consider usable here, at registration time — not when a
        // later message fails to go out. Store the provider's canonical form, not what the caller typed.
        var validation = await _smsSender.ValidateAsync(rawPhoneNumber, cancellationToken);
        if (!validation.IsValid || string.IsNullOrEmpty(validation.CanonicalNumber))
        {
            var reasons = validation.ValidationErrors.Count > 0
                ? string.Join(", ", validation.ValidationErrors)
                : "not a usable destination";
            throw new InvalidPhoneNumberException($"The number provided cannot be registered ({reasons}).");
        }

        var canonical = validation.CanonicalNumber;

        // Idempotent registration: if the shopper already has this canonical number, return it.
        var existing = await _contactNumbers.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), cancellationToken);
        var already = existing.FirstOrDefault(c => c.PhoneNumber == canonical);
        if (already is not null)
        {
            return already;
        }

        var contactNumber = new ContactNumber(ownerId, canonical);
        await _contactNumbers.AddAsync(contactNumber, cancellationToken);
        _logger.LogInformation("Registered a contact number for owner {OwnerId} (contactNumberId {ContactNumberId}).",
            ownerId, contactNumber.Id);
        return contactNumber;
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        return await _contactNumbers.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), cancellationToken);
    }

    public async Task<bool> RemoveAsync(string ownerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));

        // Scoped by owner: another shopper's number simply is not found here, so it can be neither seen
        // nor deleted through this call.
        var contactNumber = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByIdAndOwnerSpecification(contactNumberId, ownerId), cancellationToken);
        if (contactNumber is null)
        {
            return false;
        }

        await _contactNumbers.DeleteAsync(contactNumber, cancellationToken);
        _logger.LogInformation("Removed contact number {ContactNumberId} for owner {OwnerId}.", contactNumberId, ownerId);
        return true;
    }
}
