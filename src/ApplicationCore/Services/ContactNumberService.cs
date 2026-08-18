using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Manages the mobile numbers a shopper has on file. All access is scoped to the calling
/// shopper. Numbers are validated with the provider before they are stored, and stored in the
/// provider's canonical form.
/// </summary>
public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IPhoneNumberValidator _phoneNumberValidator;
    private readonly ITwilioMessagingClient _messagingClient;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        IPhoneNumberValidator phoneNumberValidator,
        ITwilioMessagingClient messagingClient,
        IAppLogger<ContactNumberService> logger)
    {
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _phoneNumberValidator = phoneNumberValidator;
        _messagingClient = messagingClient;
        _logger = logger;
    }

    public async Task<ContactNumberRegistrationResult> RegisterAsync(string buyerId, string rawNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawNumber))
        {
            return ContactNumberRegistrationResult.Rejected("A phone number is required.");
        }

        // Ask the provider whether this is a usable destination, and for its canonical form.
        var validation = await _phoneNumberValidator.ValidateAsync(rawNumber.Trim(), cancellationToken);
        if (!validation.IsValid || string.IsNullOrEmpty(validation.CanonicalE164))
        {
            var reason = validation.ValidationErrors.Count > 0
                ? string.Join(", ", validation.ValidationErrors)
                : "not a usable destination";
            // Deliberately does not echo the raw number back in logs.
            _logger.LogWarning("Rejected contact number registration for buyer {BuyerId}: {Reason}", buyerId, reason);
            return ContactNumberRegistrationResult.Rejected($"The number is not a usable destination ({reason}).");
        }

        var canonical = validation.CanonicalE164!;

        // Idempotent: registering the same canonical number again returns the existing record.
        var existing = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        var already = existing.FirstOrDefault(c => c.PhoneNumber == canonical);
        if (already is not null)
        {
            return ContactNumberRegistrationResult.Ok(already);
        }

        var contactNumber = new ContactNumber(buyerId, canonical);
        contactNumber = await _contactNumbers.AddAsync(contactNumber, cancellationToken);
        return ContactNumberRegistrationResult.Ok(contactNumber);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return numbers;
    }

    public async Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        var number = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByIdForBuyerSpecification(contactNumberId, buyerId), cancellationToken);
        if (number is null)
        {
            return false;
        }

        // Nothing may be sent to this number again: call off any not-yet-sent scheduled messages
        // aimed at it. Best-effort — failing to reach the provider must not block the removal.
        var pending = await _notifications.ListAsync(
            new PendingScheduledNotificationsForNumberSpecification(buyerId, number.PhoneNumber), cancellationToken);
        foreach (var notification in pending)
        {
            try
            {
                await _messagingClient.CancelScheduledAsync(notification.ProviderMessageSid!, cancellationToken);
                notification.MarkCanceled();
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (System.Exception ex)
            {
                _logger.LogWarning("Could not cancel scheduled notification {NotificationId} while removing a contact number: {Error}",
                    notification.Id, ex.Message);
            }
        }

        await _contactNumbers.DeleteAsync(number, cancellationToken);
        return true;
    }
}
