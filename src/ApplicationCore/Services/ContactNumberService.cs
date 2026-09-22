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
    private readonly IRepository<ContactNumber> _repository;
    private readonly ISmsProvider _smsProvider;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ContactNumber> repository,
        ISmsProvider smsProvider,
        IAppLogger<ContactNumberService> logger)
    {
        _repository = repository;
        _smsProvider = smsProvider;
        _logger = logger;
    }

    public async Task<ContactNumberRegistration> RegisterAsync(string ownerId, string rawNumber, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(rawNumber, nameof(rawNumber));

        // Reject a number the provider does not consider a usable destination here — up front —
        // rather than when a message later fails. Store the provider's canonical form.
        var validation = await _smsProvider.ValidateAsync(rawNumber, ct);
        if (!validation.IsValid || string.IsNullOrWhiteSpace(validation.CanonicalNumber))
        {
            _logger.LogWarning("Rejected a contact-number registration for owner {Owner}: not a usable destination.", ownerId);
            return ContactNumberRegistration.Rejected;
        }

        var canonical = validation.CanonicalNumber!;

        // Idempotent: if this owner already has this canonical number, return the existing one.
        var existing = await _repository.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), ct);
        var already = existing.FirstOrDefault(c => c.PhoneNumber == canonical);
        if (already is not null)
        {
            return ContactNumberRegistration.Ok(already);
        }

        var contactNumber = new ContactNumber(ownerId, canonical);
        await _repository.AddAsync(contactNumber, ct);
        _logger.LogInformation("Registered a contact number {Id} for owner {Owner}.", contactNumber.Id, ownerId);
        return ContactNumberRegistration.Ok(contactNumber);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string ownerId, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        var numbers = await _repository.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), ct);
        return numbers;
    }

    public async Task<bool> RemoveAsync(string ownerId, int contactNumberId, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));

        // The (owner, id) filter is what enforces isolation: another shopper's number never matches.
        var number = await _repository.FirstOrDefaultAsync(
            new ContactNumberByIdForOwnerSpecification(ownerId, contactNumberId), ct);
        if (number is null)
        {
            return false;
        }

        await _repository.DeleteAsync(number, ct);
        _logger.LogInformation("Removed contact number {Id} for owner {Owner}.", contactNumberId, ownerId);
        return true;
    }
}
