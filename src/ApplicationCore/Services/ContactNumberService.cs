using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _repository;
    private readonly ISmsGateway _gateway;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(IRepository<ContactNumber> repository, ISmsGateway gateway,
        IAppLogger<ContactNumberService> logger)
    {
        _repository = repository;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<ContactNumber> RegisterAsync(string ownerId, string rawNumber, CancellationToken ct)
    {
        Guard.Against.NullOrWhiteSpace(rawNumber, nameof(rawNumber));

        // Reject an unusable destination here (at registration), not when a later message fails to go out.
        var validation = await _gateway.ValidateNumberAsync(rawNumber, ct);
        if (!validation.IsValid || string.IsNullOrEmpty(validation.CanonicalNumber))
        {
            throw new InvalidPhoneNumberException("The supplied number is not a usable destination.");
        }

        var canonical = validation.CanonicalNumber!; // store the provider's canonical form, not what was typed

        var existing = await _repository.FirstOrDefaultAsync(
            new ContactNumberByOwnerAndNumberSpecification(ownerId, canonical), ct);
        if (existing is not null)
        {
            return existing; // already on file for this shopper
        }

        var entity = new ContactNumber(ownerId, canonical);
        await _repository.AddAsync(entity, ct);
        _logger.LogInformation($"Registered contact number {entity.Id} for a shopper.");
        return entity;
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string ownerId, CancellationToken ct)
        => await _repository.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), ct);

    public async Task<bool> DeleteAsync(string ownerId, int contactNumberId, CancellationToken ct)
    {
        // Scoped to the caller: a number owned by another shopper is simply not found here.
        var entity = await _repository.FirstOrDefaultAsync(
            new ContactNumberByOwnerAndIdSpecification(ownerId, contactNumberId), ct);
        if (entity is null)
        {
            return false;
        }

        await _repository.DeleteAsync(entity, ct);
        _logger.LogInformation($"Removed contact number {contactNumberId} for a shopper.");
        return true;
    }
}
