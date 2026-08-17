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

    public async Task<RegisterContactNumberResult> RegisterAsync(string ownerId, string rawNumber, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(rawNumber, nameof(rawNumber));

        // Reject a number the provider does not consider a usable destination here, at registration —
        // not at the moment a message later fails to go out.
        var lookup = await _smsProvider.LookupAsync(rawNumber, cancellationToken);
        if (!lookup.IsValid || string.IsNullOrEmpty(lookup.CanonicalE164))
        {
            // Deliberately do not log the number itself.
            _logger.LogInformation("Rejected a contact-number registration for {Owner}: not a usable destination.", ownerId);
            return new RegisterContactNumberResult(false, null, lookup.Reason ?? "The number is not a usable destination.");
        }

        // Store the provider's own canonical form, and avoid registering the same number twice.
        var existing = await _repository.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), cancellationToken);
        var already = existing.FirstOrDefault(c => c.PhoneNumber == lookup.CanonicalE164);
        if (already != null)
        {
            return new RegisterContactNumberResult(true, already, null);
        }

        var contactNumber = new ContactNumber(ownerId, lookup.CanonicalE164);
        contactNumber = await _repository.AddAsync(contactNumber, cancellationToken);
        _logger.LogInformation("Registered contact number {Id} for {Owner}.", contactNumber.Id, ownerId);
        return new RegisterContactNumberResult(true, contactNumber, null);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        var numbers = await _repository.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), cancellationToken);
        return numbers;
    }

    public async Task<bool> RemoveAsync(string ownerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));

        // Scoped by owner so one shopper can never delete another's number.
        var contactNumber = await _repository.FirstOrDefaultAsync(
            new ContactNumberByIdForOwnerSpecification(contactNumberId, ownerId), cancellationToken);
        if (contactNumber == null)
        {
            return false;
        }

        await _repository.DeleteAsync(contactNumber, cancellationToken);
        _logger.LogInformation("Removed contact number {Id} for {Owner}.", contactNumberId, ownerId);
        return true;
    }
}
