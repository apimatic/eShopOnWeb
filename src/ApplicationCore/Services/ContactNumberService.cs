using System.Collections.Generic;
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
    private readonly ISmsGateway _smsGateway;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ContactNumber> repository,
        ISmsGateway smsGateway,
        IAppLogger<ContactNumberService> logger)
    {
        _repository = repository;
        _smsGateway = smsGateway;
        _logger = logger;
    }

    public async Task<RegisterContactNumberResult> RegisterAsync(string buyerId, string rawPhoneNumber, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        if (string.IsNullOrWhiteSpace(rawPhoneNumber))
            return RegisterContactNumberResult.Rejected("A phone number is required.");

        // Validate + canonicalize with the provider (never log the raw or canonical number).
        var lookup = await _smsGateway.LookupNumberAsync(rawPhoneNumber.Trim(), ct);
        if (!lookup.IsValid || string.IsNullOrEmpty(lookup.CanonicalNumber))
        {
            _logger.LogWarning($"Rejected contact-number registration for {buyerId}: provider reports number not a usable destination.");
            return RegisterContactNumberResult.Rejected("The phone number is not a valid, reachable destination.");
        }

        // If the shopper already has this canonical number, return the existing record (no duplicate).
        var existing = await _repository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), ct);
        foreach (var e in existing)
        {
            if (e.PhoneNumber == lookup.CanonicalNumber)
                return RegisterContactNumberResult.Ok(e);
        }

        var contactNumber = new ContactNumber(buyerId, lookup.CanonicalNumber);
        contactNumber = await _repository.AddAsync(contactNumber, ct);
        _logger.LogInformation($"Registered contact number {contactNumber.Id} for {buyerId}.");
        return RegisterContactNumberResult.Ok(contactNumber);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _repository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), ct);
    }

    public async Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        // Scoped by owner: a shopper can only ever delete their own number.
        var contactNumber = await _repository.FirstOrDefaultAsync(
            new ContactNumberByIdForBuyerSpecification(contactNumberId, buyerId), ct);
        if (contactNumber is null)
            return false;

        await _repository.DeleteAsync(contactNumber, ct);
        _logger.LogInformation($"Deleted contact number {contactNumberId} for {buyerId}.");
        return true;
    }
}
