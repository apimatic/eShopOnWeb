using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly ISmsProvider _smsProvider;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        ISmsProvider smsProvider,
        IRepository<ContactNumber> contactNumbers,
        IAppLogger<ContactNumberService> logger)
    {
        _smsProvider = smsProvider;
        _contactNumbers = contactNumbers;
        _logger = logger;
    }

    public async Task<ContactNumberRegistrationResult> RegisterAsync(string buyerId, string rawNumber, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(rawNumber, nameof(rawNumber));

        // Validate & canonicalize with the provider up front — reject a number the provider does not
        // consider a usable destination here, not when a later message fails to go out.
        var lookup = await _smsProvider.LookupAsync(rawNumber, cancellationToken);
        if (!lookup.IsValid || string.IsNullOrEmpty(lookup.CanonicalPhoneNumber))
        {
            // The raw/canonical number is PII and is never logged; only the provider's reason codes are.
            _logger.LogWarning("Contact-number registration rejected for buyer {BuyerId}: not a usable destination [{Reasons}].",
                buyerId, string.Join(",", lookup.ValidationErrors));
            var reason = lookup.ValidationErrors.Any()
                ? $"The number is not a usable SMS destination ({string.Join(", ", lookup.ValidationErrors)})."
                : "The number is not a usable SMS destination.";
            return ContactNumberRegistrationResult.Rejected(reason);
        }

        var canonical = lookup.CanonicalPhoneNumber;

        // Store the provider's canonical form, not whatever the caller typed. If the shopper already
        // has this number on file, return the existing record (avoids duplicate, double-messaged numbers).
        var existing = await _contactNumbers.FirstOrDefaultAsync(
            new ExistingContactNumberSpecification(buyerId, canonical), cancellationToken);
        if (existing is not null)
        {
            return ContactNumberRegistrationResult.Registered(existing);
        }

        var created = await _contactNumbers.AddAsync(new ContactNumber(buyerId, canonical), cancellationToken);
        _logger.LogInformation("Registered contact number {ContactNumberId} for buyer {BuyerId}.", created.Id, buyerId);
        return ContactNumberRegistrationResult.Registered(created);
    }
}
