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

    public async Task<RegisterContactNumberResult> RegisterAsync(string buyerId, string rawPhoneNumber, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        if (string.IsNullOrWhiteSpace(rawPhoneNumber))
        {
            return RegisterContactNumberResult.Rejected("A phone number is required.");
        }

        // Reject a number the provider does not consider a usable destination here, at registration,
        // rather than at the moment a message later fails to go out.
        var validation = await _smsProvider.ValidateNumberAsync(rawPhoneNumber, cancellationToken);
        if (!validation.IsValid || string.IsNullOrEmpty(validation.CanonicalNumber))
        {
            var reasons = validation.ValidationErrors.Count > 0
                ? string.Join(", ", validation.ValidationErrors)
                : "the number is not a valid destination";
            // Note: the rejected number itself is deliberately never logged.
            _logger.LogInformation($"Rejected a contact number registration for buyer '{buyerId}': {reasons}.");
            return RegisterContactNumberResult.Rejected($"The phone number was rejected: {reasons}.");
        }

        // Store the provider's canonical form, not whatever the caller typed.
        var canonical = validation.CanonicalNumber;

        // If the shopper already has this exact number on file, treat the registration as idempotent.
        var existing = await _repository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        var duplicate = existing.FirstOrDefault(c => c.PhoneNumber == canonical);
        if (duplicate is not null)
        {
            return RegisterContactNumberResult.Success(duplicate);
        }

        var contactNumber = new ContactNumber(buyerId, canonical);
        contactNumber = await _repository.AddAsync(contactNumber, cancellationToken);
        _logger.LogInformation($"Registered contact number {contactNumber.Id} for buyer '{buyerId}'.");
        return RegisterContactNumberResult.Success(contactNumber);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var numbers = await _repository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return numbers;
    }

    public async Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var contactNumber = await _repository.GetByIdAsync(contactNumberId, cancellationToken);

        // A number belongs to the shopper who registered it: never let one shopper delete another's.
        if (contactNumber is null || contactNumber.BuyerId != buyerId)
        {
            return false;
        }

        await _repository.DeleteAsync(contactNumber, cancellationToken);
        _logger.LogInformation($"Deleted contact number {contactNumberId} for buyer '{buyerId}'.");
        return true;
    }
}
