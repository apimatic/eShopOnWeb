using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

    public ContactNumberService(IRepository<ContactNumber> contactNumberRepository,
        IPhoneNumberValidator phoneNumberValidator,
        IAppLogger<ContactNumberService> logger)
    {
        _contactNumberRepository = contactNumberRepository;
        _phoneNumberValidator = phoneNumberValidator;
        _logger = logger;
    }

    public async Task<Result<ContactNumber>> RegisterAsync(string buyerId, string rawNumber,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawNumber))
            return Result<ContactNumber>.Invalid(new List<ValidationError>
            {
                new() { Identifier = "phoneNumber", ErrorMessage = "A phone number is required." }
            });

        // Reject a number the provider does not consider a usable destination here, at registration,
        // rather than at the moment a message fails to go out.
        var validation = await _phoneNumberValidator.ValidateAsync(rawNumber, cancellationToken);
        if (!validation.IsValid || string.IsNullOrWhiteSpace(validation.CanonicalNumber))
        {
            var reason = validation.Errors.Any()
                ? $"The number is not a usable destination ({string.Join(", ", validation.Errors)})."
                : "The number is not a usable destination.";
            _logger.LogInformation("Rejected a contact number registration: number is not a usable destination.");
            return Result<ContactNumber>.Invalid(new List<ValidationError>
            {
                new() { Identifier = "phoneNumber", ErrorMessage = reason }
            });
        }

        // Store the provider's canonical form, not whatever the caller typed. De-duplicate per owner.
        var existing = await _contactNumberRepository.ListAsync(
            new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        var already = existing.FirstOrDefault(c => c.PhoneNumber == validation.CanonicalNumber);
        if (already is not null)
            return Result<ContactNumber>.Success(already);

        var contactNumber = new ContactNumber(buyerId, validation.CanonicalNumber!);
        await _contactNumberRepository.AddAsync(contactNumber, cancellationToken);
        _logger.LogInformation($"Registered a contact number for buyer (id={contactNumber.Id}).");
        return Result<ContactNumber>.Success(contactNumber);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        return await _contactNumberRepository.ListAsync(
            new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task<Result> DeleteAsync(string buyerId, int contactNumberId,
        CancellationToken cancellationToken = default)
    {
        // Owner-scoped lookup: a shopper can only delete their own number.
        var contactNumber = await _contactNumberRepository.FirstOrDefaultAsync(
            new ContactNumberByIdForBuyerSpecification(contactNumberId, buyerId), cancellationToken);
        if (contactNumber is null)
            return Result.NotFound();

        await _contactNumberRepository.DeleteAsync(contactNumber, cancellationToken);
        _logger.LogInformation($"Removed a contact number for buyer (id={contactNumberId}).");
        return Result.Success();
    }
}
