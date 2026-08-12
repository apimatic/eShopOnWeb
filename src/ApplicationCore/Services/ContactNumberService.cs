using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly ITwilioMessagingGateway _gateway;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ContactNumber> contactNumbers,
        ITwilioMessagingGateway gateway,
        IAppLogger<ContactNumberService> logger)
    {
        _contactNumbers = contactNumbers;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<ContactNumber> RegisterAsync(string buyerId, string phoneNumber, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(phoneNumber, nameof(phoneNumber));

        // Reject a number the provider does not consider a usable destination here, at registration —
        // not when a later message fails. Store the provider's own canonical form, not the raw input.
        var validation = await _gateway.ValidateNumberAsync(phoneNumber, ct);
        if (!validation.IsValid || string.IsNullOrWhiteSpace(validation.CanonicalE164))
        {
            _logger.LogWarning("Rejected a contact-number registration for a shopper: not a usable destination.");
            throw new InvalidPhoneNumberException(validation.Reasons);
        }

        // If the shopper already has this exact number on file, return it rather than duplicating.
        var existing = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), ct);
        var duplicate = existing.FirstOrDefault(c => c.E164Number == validation.CanonicalE164);
        if (duplicate is not null)
        {
            return duplicate;
        }

        var contactNumber = new ContactNumber(buyerId, validation.CanonicalE164);
        await _contactNumbers.AddAsync(contactNumber, ct);
        _logger.LogInformation("Registered contact number {ContactNumberId} for a shopper.", contactNumber.Id);
        return contactNumber;
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), ct);
    }

    public async Task<bool> RemoveAsync(string buyerId, int contactNumberId, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        // Scoped to the owner: one shopper can never delete another's number.
        var contactNumber = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndIdSpecification(buyerId, contactNumberId), ct);
        if (contactNumber is null)
        {
            return false;
        }

        await _contactNumbers.DeleteAsync(contactNumber, ct);
        _logger.LogInformation("Removed contact number {ContactNumberId} for a shopper.", contactNumberId);
        return true;
    }
}
