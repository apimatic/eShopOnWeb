using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IPhoneNumberValidator _phoneNumberValidator;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ContactNumber> contactNumberRepository,
        IPhoneNumberValidator phoneNumberValidator,
        IAppLogger<ContactNumberService> logger)
    {
        _contactNumberRepository = contactNumberRepository;
        _phoneNumberValidator = phoneNumberValidator;
        _logger = logger;
    }

    public async Task<Result<ContactNumber>> RegisterAsync(string ownerId, string rawPhoneNumber, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));

        if (string.IsNullOrWhiteSpace(rawPhoneNumber))
        {
            return Result<ContactNumber>.Invalid(new List<ValidationError> { new() { ErrorMessage = "A phone number is required." } });
        }

        // Reject a number the provider does not consider a usable destination here, up front — rather than
        // at the moment a message later fails to go out.
        PhoneNumberValidationResultOrError validation;
        try
        {
            var result = await _phoneNumberValidator.ValidateAsync(rawPhoneNumber, cancellationToken);
            validation = new PhoneNumberValidationResultOrError(result.IsValid, result.CanonicalNumber, result.Errors);
        }
        catch (System.Exception)
        {
            // The number is never logged, including on failure.
            _logger.LogWarning("Could not validate a contact number with the provider for owner {OwnerId}.", ownerId);
            return Result<ContactNumber>.Invalid(new List<ValidationError>
            {
                new() { ErrorMessage = "The phone number could not be validated with the provider. Please try again." }
            });
        }

        if (!validation.IsValid || string.IsNullOrEmpty(validation.CanonicalNumber))
        {
            var errors = validation.Errors.Count > 0
                ? string.Join(", ", validation.Errors)
                : "not a usable destination";
            return Result<ContactNumber>.Invalid(new List<ValidationError>
            {
                new() { ErrorMessage = $"The phone number is not valid ({errors})." }
            });
        }

        // Store the provider's own canonical form, not whatever the caller typed.
        var existing = await _contactNumberRepository.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), cancellationToken);
        var alreadyRegistered = existing.FirstOrDefault(c => c.PhoneNumber == validation.CanonicalNumber);
        if (alreadyRegistered is not null)
        {
            // Idempotent from the shopper's point of view: the number is already on file.
            return Result<ContactNumber>.Success(alreadyRegistered);
        }

        var contactNumber = new ContactNumber(ownerId, validation.CanonicalNumber);
        contactNumber = await _contactNumberRepository.AddAsync(contactNumber, cancellationToken);

        _logger.LogInformation("Registered contact number {ContactNumberId} for owner {OwnerId}.", contactNumber.Id, ownerId);
        return Result<ContactNumber>.Success(contactNumber);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        var numbers = await _contactNumberRepository.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), cancellationToken);
        return numbers;
    }

    public async Task<Result> RemoveAsync(string ownerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));

        // Scoped by owner so one shopper can never delete another's number.
        var contactNumber = await _contactNumberRepository.FirstOrDefaultAsync(
            new ContactNumberByIdForOwnerSpecification(contactNumberId, ownerId), cancellationToken);

        if (contactNumber is null)
        {
            return Result.NotFound();
        }

        await _contactNumberRepository.DeleteAsync(contactNumber, cancellationToken);
        _logger.LogInformation("Removed contact number {ContactNumberId} for owner {OwnerId}.", contactNumberId, ownerId);
        return Result.Success();
    }

    private readonly struct PhoneNumberValidationResultOrError
    {
        public PhoneNumberValidationResultOrError(bool isValid, string? canonicalNumber, IReadOnlyList<string> errors)
        {
            IsValid = isValid;
            CanonicalNumber = canonicalNumber;
            Errors = errors;
        }

        public bool IsValid { get; }
        public string? CanonicalNumber { get; }
        public IReadOnlyList<string> Errors { get; }
    }
}
