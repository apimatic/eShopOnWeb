using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly ITwilioMessagingClient _twilio;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ContactNumber> contactNumbers,
        ITwilioMessagingClient twilio,
        IAppLogger<ContactNumberService> logger)
    {
        _contactNumbers = contactNumbers;
        _twilio = twilio;
        _logger = logger;
    }

    public async Task<RegisterContactNumberResult> RegisterAsync(string buyerId, string rawPhoneNumber, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(rawPhoneNumber, nameof(rawPhoneNumber));

        // Validate with the provider up front and keep its canonical form. A number the provider
        // does not consider usable is rejected here, not when a message later fails to go out.
        var lookup = await _twilio.LookupPhoneNumberAsync(rawPhoneNumber, cancellationToken);
        if (!lookup.Valid || string.IsNullOrEmpty(lookup.PhoneNumber))
        {
            _logger.LogWarning("Rejected a contact number registration for buyer {BuyerId}: provider marked it not a usable destination.", buyerId);
            var errors = lookup.ValidationErrors.Count > 0
                ? lookup.ValidationErrors
                : new List<string> { "NOT_A_VALID_DESTINATION" };
            return RegisterContactNumberResult.Rejected(errors);
        }

        var canonical = lookup.PhoneNumber;

        // Avoid storing the same canonical number twice for one shopper.
        var existing = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        var duplicate = existing.FirstOrDefault(c => c.PhoneNumber == canonical);
        if (duplicate is not null)
        {
            return RegisterContactNumberResult.Success(duplicate);
        }

        var contactNumber = new ContactNumber(buyerId, canonical);
        contactNumber = await _contactNumbers.AddAsync(contactNumber, cancellationToken);
        _logger.LogInformation("Registered a contact number (id {ContactNumberId}) for buyer {BuyerId}.", contactNumber.Id, buyerId);
        return RegisterContactNumberResult.Success(contactNumber);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var contactNumber = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByIdForBuyerSpecification(buyerId, contactNumberId), cancellationToken);
        if (contactNumber is null)
        {
            return false;
        }

        await _contactNumbers.DeleteAsync(contactNumber, cancellationToken);
        _logger.LogInformation("Removed contact number {ContactNumberId} for buyer {BuyerId}.", contactNumberId, buyerId);
        return true;
    }
}
