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
    private readonly ISmsGateway _smsGateway;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ContactNumber> contactNumbers,
        ISmsGateway smsGateway,
        IAppLogger<ContactNumberService> logger)
    {
        _contactNumbers = contactNumbers;
        _smsGateway = smsGateway;
        _logger = logger;
    }

    public async Task<ContactNumber> RegisterAsync(string buyerId, string rawNumber, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(rawNumber, nameof(rawNumber));

        // Reject an unusable destination here, at registration, rather than when a message fails.
        var validation = await _smsGateway.ValidateNumberAsync(rawNumber, cancellationToken);
        if (!validation.IsValid || string.IsNullOrEmpty(validation.CanonicalNumber))
        {
            throw new InvalidPhoneNumberException(
                validation.Reason ?? "The supplied number is not a usable SMS destination.");
        }

        // Store the provider's canonical E.164 form, not the caller's raw text. Idempotent per shopper.
        var existing = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        var duplicate = existing.FirstOrDefault(c => c.PhoneNumber == validation.CanonicalNumber);
        if (duplicate is not null)
        {
            return duplicate;
        }

        var contactNumber = new ContactNumber(buyerId, validation.CanonicalNumber);
        await _contactNumbers.AddAsync(contactNumber, cancellationToken);
        _logger.LogInformation("Registered contact number {ContactNumberId} for a shopper.", contactNumber.Id);
        return contactNumber;
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return numbers;
    }

    public async Task RemoveAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        // Ownership is enforced in the query: another shopper's number simply is not found.
        var contactNumber = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByIdForBuyerSpecification(contactNumberId, buyerId), cancellationToken);
        if (contactNumber is null)
        {
            throw new EntityNotFoundException($"Contact number {contactNumberId} was not found.");
        }

        await _contactNumbers.DeleteAsync(contactNumber, cancellationToken);
        _logger.LogInformation("Removed contact number {ContactNumberId} for a shopper.", contactNumberId);
    }
}
