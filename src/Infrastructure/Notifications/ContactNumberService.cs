using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Notifications;

/// <summary>Flow 1 implementation. All operations are scoped to the owning shopper.</summary>
public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _repository;
    private readonly ISmsProviderGateway _gateway;
    private readonly ILogger<ContactNumberService> _logger;

    public ContactNumberService(IRepository<ContactNumber> repository, ISmsProviderGateway gateway,
        ILogger<ContactNumberService> logger)
    {
        _repository = repository;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<RegisterContactNumberResult> RegisterAsync(string ownerId, string phoneNumber,
        CancellationToken ct)
    {
        PhoneValidationResult validation;
        try
        {
            validation = await _gateway.ValidateAsync(phoneNumber, ct);
        }
        catch (SmsProviderException ex)
        {
            _logger.LogWarning(ex, "Could not validate a contact number with the provider.");
            return new RegisterContactNumberResult(RegisterContactNumberOutcome.ProviderUnavailable, null, null,
                "The number could not be validated with the provider. Please try again later.");
        }

        if (!validation.IsUsable || string.IsNullOrWhiteSpace(validation.CanonicalNumber))
        {
            return new RegisterContactNumberResult(RegisterContactNumberOutcome.RejectedUnusable, null, null,
                validation.Reason ?? "The number is not a usable destination.");
        }

        var canonical = validation.CanonicalNumber;

        // If this shopper already has this canonical number, treat register as idempotent (no duplicate row).
        var existing = await _repository.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), ct);
        var already = existing.FirstOrDefault(c => c.E164Number == canonical);
        if (already is not null)
        {
            return new RegisterContactNumberResult(RegisterContactNumberOutcome.Registered, already.Id, canonical,
                "Number already registered.");
        }

        var entity = new ContactNumber(ownerId, canonical);
        await _repository.AddAsync(entity, ct);
        _logger.LogInformation("Registered contact number {ContactNumberId} for a shopper.", entity.Id);
        return new RegisterContactNumberResult(RegisterContactNumberOutcome.Registered, entity.Id, canonical, null);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string ownerId, CancellationToken ct)
    {
        return await _repository.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), ct);
    }

    public async Task<bool> DeleteAsync(string ownerId, int contactNumberId, CancellationToken ct)
    {
        var entity = await _repository.FirstOrDefaultAsync(
            new ContactNumberByOwnerAndIdSpecification(ownerId, contactNumberId), ct);
        if (entity is null)
        {
            return false;
        }

        await _repository.DeleteAsync(entity, ct);
        _logger.LogInformation("Removed contact number {ContactNumberId} for a shopper.", contactNumberId);
        return true;
    }
}
