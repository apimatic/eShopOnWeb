using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Sms;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly ISmsGateway _smsGateway;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ContactNumber> contactNumberRepository,
        ISmsGateway smsGateway,
        IAppLogger<ContactNumberService> logger)
    {
        _contactNumberRepository = contactNumberRepository;
        _smsGateway = smsGateway;
        _logger = logger;
    }

    public async Task<ContactNumberRegistration> RegisterAsync(string buyerId, string rawNumber, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        if (string.IsNullOrWhiteSpace(rawNumber))
        {
            return ContactNumberRegistration.Rejected(new[] { "A phone number is required." });
        }

        // Reject an unusable destination up front (rather than when a message later fails), and keep the
        // provider's own canonical form of the number rather than whatever the caller typed.
        var lookup = await _smsGateway.LookupAsync(rawNumber, cancellationToken);
        if (!lookup.IsValid || string.IsNullOrEmpty(lookup.CanonicalNumber))
        {
            var errors = lookup.ValidationErrors.Count > 0
                ? lookup.ValidationErrors
                : new[] { "The number is not a valid destination." };
            _logger.LogWarning($"Rejected contact number registration for buyer; provider deemed it invalid.");
            return ContactNumberRegistration.Rejected(errors);
        }

        var canonical = lookup.CanonicalNumber;

        // Avoid storing the same number twice for one shopper.
        var existing = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        var duplicate = existing.FirstOrDefault(c => c.PhoneNumber == canonical);
        if (duplicate is not null)
        {
            return ContactNumberRegistration.Success(duplicate);
        }

        var contactNumber = new ContactNumber(buyerId, canonical);
        contactNumber = await _contactNumberRepository.AddAsync(contactNumber, cancellationToken);
        _logger.LogInformation($"Registered contact number {contactNumber.Id} for a shopper.");
        return ContactNumberRegistration.Success(contactNumber);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var numbers = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return numbers;
    }

    public async Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var contactNumber = await _contactNumberRepository.GetByIdAsync(contactNumberId, cancellationToken);

        // A number belongs to the shopper who registered it: never let one shopper delete another's, and do
        // not reveal that it exists.
        if (contactNumber is null || contactNumber.BuyerId != buyerId)
        {
            return false;
        }

        await _contactNumberRepository.DeleteAsync(contactNumber, cancellationToken);
        _logger.LogInformation($"Removed contact number {contactNumberId} for a shopper.");
        return true;
    }
}
