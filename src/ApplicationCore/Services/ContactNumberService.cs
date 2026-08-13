using System.Collections.Generic;
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

    public async Task<ContactNumber> RegisterAsync(string ownerId, string rawNumber)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(rawNumber, nameof(rawNumber));

        // Reject an unusable destination here, up front — not later when a message fails to go out.
        var lookup = await _smsProvider.LookupAsync(rawNumber);
        if (!lookup.Valid || string.IsNullOrEmpty(lookup.CanonicalNumber))
        {
            _logger.LogWarning("Rejected a contact-number registration for owner {OwnerId}: not a usable destination.", ownerId);
            throw new InvalidPhoneNumberException();
        }

        // Store the provider's own canonical form, not whatever the caller typed.
        var contactNumber = new ContactNumber(ownerId, lookup.CanonicalNumber);
        await _repository.AddAsync(contactNumber);

        _logger.LogInformation("Registered contact number {ContactNumberId} for owner {OwnerId}.", contactNumber.Id, ownerId);
        return contactNumber;
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string ownerId)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        return await _repository.ListAsync(new ContactNumbersByOwnerSpecification(ownerId));
    }

    public async Task<bool> DeleteAsync(string ownerId, int contactNumberId)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));

        var contactNumber = await _repository.GetByIdAsync(contactNumberId);

        // A number belongs to the shopper who registered it: silently treat another owner's number
        // (or a missing one) as not found, so ownership is never revealed.
        if (contactNumber is null || contactNumber.OwnerId != ownerId)
        {
            return false;
        }

        await _repository.DeleteAsync(contactNumber);
        _logger.LogInformation("Deleted contact number {ContactNumberId} for owner {OwnerId}.", contactNumberId, ownerId);
        return true;
    }
}
