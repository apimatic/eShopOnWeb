using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly ITwilioMessagingService _twilio;
    private readonly IOrderNotificationService _orderNotifications;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ContactNumber> contactNumbers,
        ITwilioMessagingService twilio,
        IOrderNotificationService orderNotifications,
        IAppLogger<ContactNumberService> logger)
    {
        _contactNumbers = contactNumbers;
        _twilio = twilio;
        _orderNotifications = orderNotifications;
        _logger = logger;
    }

    public async Task<RegisterContactNumberResult> RegisterAsync(string buyerId, string rawPhoneNumber, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        if (string.IsNullOrWhiteSpace(rawPhoneNumber))
        {
            return RegisterContactNumberResult.Rejected("A phone number is required.");
        }

        // Reject an unusable destination here, before any message is ever attempted, and keep the
        // provider's own canonical form of the number rather than whatever the caller typed.
        PhoneNumberLookupResult lookup;
        try
        {
            lookup = await _twilio.LookupAsync(rawPhoneNumber.Trim(), cancellationToken);
        }
        catch
        {
            // Do not surface any part of the number in logs.
            _logger.LogWarning("Contact number validation could not be completed with the provider for buyer {BuyerId}.", buyerId);
            return RegisterContactNumberResult.Rejected("The number could not be validated with the provider. Please try again.");
        }

        if (!lookup.IsValid || string.IsNullOrEmpty(lookup.CanonicalE164))
        {
            return RegisterContactNumberResult.Rejected("The number is not a usable message destination.");
        }

        var canonical = lookup.CanonicalE164;

        // Avoid storing (and later double-messaging) the same number twice for one shopper.
        var existing = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        var duplicate = existing.FirstOrDefault(c => c.PhoneNumber == canonical);
        if (duplicate is not null)
        {
            return RegisterContactNumberResult.Ok(duplicate);
        }

        var contactNumber = new ContactNumber(buyerId, canonical);
        await _contactNumbers.AddAsync(contactNumber, cancellationToken);
        _logger.LogInformation("Registered a contact number (id {ContactNumberId}) for buyer {BuyerId}.", contactNumber.Id, buyerId);
        return RegisterContactNumberResult.Ok(contactNumber);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task<bool> RemoveAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var contactNumber = await _contactNumbers.GetByIdAsync(contactNumberId, cancellationToken);

        // A number belongs to the shopper who registered it: one shopper must never remove another's.
        if (contactNumber is null || contactNumber.BuyerId != buyerId)
        {
            return false;
        }

        // Nothing may be sent to it again: call off anything still queued for a future send to it.
        await _orderNotifications.CancelScheduledMessagesToNumberAsync(buyerId, contactNumber.PhoneNumber, cancellationToken);

        await _contactNumbers.DeleteAsync(contactNumber, cancellationToken);
        _logger.LogInformation("Removed contact number (id {ContactNumberId}) for buyer {BuyerId}.", contactNumberId, buyerId);
        return true;
    }
}
