using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly ISmsProvider _smsProvider;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ContactNumber> contactNumbers,
        ISmsProvider smsProvider,
        IAppLogger<ContactNumberService> logger)
    {
        _contactNumbers = contactNumbers;
        _smsProvider = smsProvider;
        _logger = logger;
    }

    public async Task<RegisterContactNumberResult> RegisterAsync(string buyerId, string rawNumber, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        if (string.IsNullOrWhiteSpace(rawNumber))
            return RegisterContactNumberResult.Rejected("A phone number is required.");

        // Ask the provider to validate and canonicalize before we ever store or send anything.
        var lookup = await _smsProvider.LookupAsync(rawNumber, cancellationToken);
        if (!lookup.IsValid || string.IsNullOrEmpty(lookup.CanonicalNumber))
        {
            // Note: the number itself is never logged.
            _logger.LogWarning("Rejected contact-number registration for buyer {BuyerId}: {Reason}",
                buyerId, lookup.ValidationError ?? "not a usable SMS destination");
            return RegisterContactNumberResult.Rejected(lookup.ValidationError ?? "The number is not a usable SMS destination.");
        }

        var canonical = lookup.CanonicalNumber;

        // Idempotent on the canonical form: registering the same number twice returns the same record.
        var existing = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        var duplicate = existing.FirstOrDefault(c => c.PhoneNumber == canonical);
        if (duplicate is not null)
            return RegisterContactNumberResult.Registered(duplicate.Id, canonical);

        var contactNumber = new ContactNumber(buyerId, canonical);
        contactNumber = await _contactNumbers.AddAsync(contactNumber, cancellationToken);

        _logger.LogInformation("Registered a contact number (id {ContactNumberId}) for buyer {BuyerId}",
            contactNumber.Id, buyerId);

        return RegisterContactNumberResult.Registered(contactNumber.Id, canonical);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var contactNumber = await _contactNumbers.GetByIdAsync(contactNumberId, cancellationToken);

        // A number belongs only to the shopper who registered it: another shopper's number is treated
        // as if it does not exist — never revealed, never deleted.
        if (contactNumber is null || contactNumber.BuyerId != buyerId)
            return false;

        await _contactNumbers.DeleteAsync(contactNumber, cancellationToken);
        _logger.LogInformation("Removed contact number {ContactNumberId} for buyer {BuyerId}", contactNumberId, buyerId);
        return true;
    }
}
