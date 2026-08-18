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
    private readonly IPhoneNumberValidator _validator;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ContactNumber> repository,
        IPhoneNumberValidator validator,
        IAppLogger<ContactNumberService> logger)
    {
        _repository = repository;
        _validator = validator;
        _logger = logger;
    }

    public async Task<RegisterContactNumberResult> RegisterAsync(string ownerId, string rawNumber, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(rawNumber, nameof(rawNumber));

        // A number the provider does not consider a usable destination is rejected here, up front,
        // rather than at the moment a message later fails to go out.
        var validation = await _validator.ValidateAsync(rawNumber, cancellationToken);
        if (!validation.IsValid || string.IsNullOrEmpty(validation.CanonicalE164))
        {
            _logger.LogWarning("Rejected a contact-number registration for owner {OwnerId}: number not usable.", ownerId);
            var errors = validation.ValidationErrors.Count > 0
                ? validation.ValidationErrors
                : new[] { "The number is not a usable message destination." };
            return RegisterContactNumberResult.Rejected(errors);
        }

        var canonical = validation.CanonicalE164;

        // Store the provider's canonical form; if the shopper already has this exact number, keep it idempotent.
        var existing = await _repository.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), cancellationToken);
        var alreadyRegistered = existing.FirstOrDefault(c => c.E164Number == canonical);
        if (alreadyRegistered is not null)
        {
            return RegisterContactNumberResult.Ok(alreadyRegistered);
        }

        var contactNumber = new ContactNumber(ownerId, canonical);
        await _repository.AddAsync(contactNumber, cancellationToken);
        _logger.LogInformation("Registered contact number {ContactNumberId} for owner {OwnerId}.", contactNumber.Id, ownerId);
        return RegisterContactNumberResult.Ok(contactNumber);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string ownerId, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        var numbers = await _repository.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), cancellationToken);
        return numbers;
    }

    public async Task<bool> DeleteAsync(string ownerId, int contactNumberId, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));

        // Scope the lookup to the owner so one shopper can never delete another's number.
        var number = await _repository.FirstOrDefaultAsync(
            new ContactNumberByOwnerAndIdSpecification(ownerId, contactNumberId), cancellationToken);
        if (number is null)
        {
            return false;
        }

        await _repository.DeleteAsync(number, cancellationToken);
        _logger.LogInformation("Removed contact number {ContactNumberId} for owner {OwnerId}.", contactNumberId, ownerId);
        return true;
    }
}
