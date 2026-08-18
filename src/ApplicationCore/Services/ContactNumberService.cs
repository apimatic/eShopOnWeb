using System.Collections.Generic;
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
    private readonly ISmsGateway _gateway;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ContactNumber> repository,
        ISmsGateway gateway,
        IAppLogger<ContactNumberService> logger)
    {
        _repository = repository;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<ContactNumberRegistrationResult> RegisterAsync(string ownerId, string rawNumber, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(rawNumber, nameof(rawNumber));

        // Ask the provider whether this is a usable destination, and get its canonical form.
        // Reject here rather than at the moment a message later fails to go out. The number is
        // never logged.
        var validation = await _gateway.ValidateNumberAsync(rawNumber, cancellationToken);
        if (!validation.IsValid || string.IsNullOrEmpty(validation.CanonicalNumber))
        {
            _logger.LogInformation("Contact number registration rejected for {Owner}: not a usable destination.", ownerId);
            return new ContactNumberRegistrationResult(Rejected: true, ContactNumber: null);
        }

        // Store the provider's canonical E.164 form, not whatever the caller typed.
        var contactNumber = new ContactNumber(ownerId, validation.CanonicalNumber);
        await _repository.AddAsync(contactNumber, cancellationToken);
        _logger.LogInformation("Registered contact number {Id} for {Owner}.", contactNumber.Id, ownerId);
        return new ContactNumberRegistrationResult(Rejected: false, ContactNumber: contactNumber);
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

        // Scope by owner: a shopper can only delete their own number.
        var contactNumber = await _repository.FirstOrDefaultAsync(
            new ContactNumberByIdForOwnerSpecification(ownerId, contactNumberId), cancellationToken);
        if (contactNumber is null)
            return false;

        await _repository.DeleteAsync(contactNumber, cancellationToken);
        _logger.LogInformation("Deleted contact number {Id} for {Owner}.", contactNumberId, ownerId);
        return true;
    }
}
