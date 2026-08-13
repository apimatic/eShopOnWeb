using System.Collections.Generic;
using System.Linq;
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
    private readonly ISmsMessagingService _sms;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ContactNumber> repository,
        ISmsMessagingService sms,
        IAppLogger<ContactNumberService> logger)
    {
        _repository = repository;
        _sms = sms;
        _logger = logger;
    }

    public async Task<ContactNumber> RegisterAsync(string ownerId, string rawNumber, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(rawNumber, nameof(rawNumber));

        // Reject a number the provider does not consider a usable destination here, at registration,
        // rather than at the moment a message fails to go out.
        var validation = await _sms.ValidateNumberAsync(rawNumber, cancellationToken);
        if (!validation.IsValid || string.IsNullOrEmpty(validation.CanonicalNumber))
        {
            throw new InvalidPhoneNumberException();
        }

        // What we store is the provider's own canonical form, not whatever the caller typed.
        var canonical = validation.CanonicalNumber;

        var existing = await _repository.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), cancellationToken);
        var duplicate = existing.FirstOrDefault(c => c.PhoneNumber == canonical);
        if (duplicate is not null)
        {
            return duplicate;
        }

        var contactNumber = new ContactNumber(ownerId, canonical);
        await _repository.AddAsync(contactNumber, cancellationToken);

        // Note: the number itself is deliberately never logged.
        _logger.LogInformation($"Registered contact number {contactNumber.Id} for a shopper.");
        return contactNumber;
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        return await _repository.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), cancellationToken);
    }

    public async Task<bool> RemoveAsync(string ownerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));

        // Scoped to the owner, so one shopper can never remove another's number.
        var contactNumber = await _repository.FirstOrDefaultAsync(
            new ContactNumberByIdForOwnerSpecification(contactNumberId, ownerId), cancellationToken);
        if (contactNumber is null)
        {
            return false;
        }

        await _repository.DeleteAsync(contactNumber, cancellationToken);
        _logger.LogInformation($"Removed contact number {contactNumberId} for a shopper.");
        return true;
    }

    public async Task<ContactNumber?> GetReachableNumberAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(ownerId))
        {
            return null;
        }

        // Most recently registered number wins (the spec orders by RegisteredAt descending).
        var numbers = await _repository.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), cancellationToken);
        return numbers.FirstOrDefault();
    }
}
