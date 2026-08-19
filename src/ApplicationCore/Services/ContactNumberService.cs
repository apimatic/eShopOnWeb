using System;
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
    private readonly ITwilioPhoneLookupClient _lookupClient;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ContactNumber> repository,
        ITwilioPhoneLookupClient lookupClient,
        IAppLogger<ContactNumberService> logger)
    {
        _repository = repository;
        _lookupClient = lookupClient;
        _logger = logger;
    }

    public async Task<ContactNumber> RegisterAsync(string buyerId, string rawPhoneNumber, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(rawPhoneNumber, nameof(rawPhoneNumber));

        // Reject an unusable destination here, at registration — not when a message later fails.
        var lookup = await _lookupClient.LookupAsync(rawPhoneNumber, cancellationToken);
        if (!lookup.Valid || string.IsNullOrWhiteSpace(lookup.PhoneNumber))
        {
            _logger.LogWarning("Rejected a contact-number registration: provider did not consider it a usable destination.");
            throw new PhoneNumberValidationException(
                "The phone number provided is not a valid, reachable destination and was not registered.");
        }

        // Store the provider's own canonical form, not whatever the caller typed.
        var canonical = lookup.PhoneNumber!;

        // Don't stack duplicates of the same canonical number for the same shopper.
        var existing = await _repository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        var duplicate = existing.FirstOrDefault(c => c.PhoneNumber == canonical);
        if (duplicate is not null)
            return duplicate;

        var contactNumber = new ContactNumber(buyerId, canonical);
        await _repository.AddAsync(contactNumber, cancellationToken);
        _logger.LogInformation("Registered a contact number (id {ContactNumberId}) for a shopper.", contactNumber.Id);
        return contactNumber;
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var list = await _repository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return list;
    }

    public async Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        // Scoped by owner so one shopper can never delete another's number.
        var contactNumber = await _repository.FirstOrDefaultAsync(
            new ContactNumberByIdForBuyerSpecification(contactNumberId, buyerId), cancellationToken);
        if (contactNumber is null)
            return false;

        await _repository.DeleteAsync(contactNumber, cancellationToken);
        _logger.LogInformation("Removed a contact number (id {ContactNumberId}) for a shopper.", contactNumberId);
        return true;
    }
}
