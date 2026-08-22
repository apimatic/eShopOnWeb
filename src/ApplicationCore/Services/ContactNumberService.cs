using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ShopperContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IPhoneNumberLookupService _lookup;
    private readonly ISmsGateway _smsGateway;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ShopperContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        IPhoneNumberLookupService lookup,
        ISmsGateway smsGateway,
        IAppLogger<ContactNumberService> logger)
    {
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _lookup = lookup;
        _smsGateway = smsGateway;
        _logger = logger;
    }

    public async Task<ContactNumberRegistrationResult> RegisterAsync(string buyerId, string phoneNumber, string? countryCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return new ContactNumberRegistrationResult
            {
                Succeeded = false,
                StatusCode = 400,
                Error = "A phone number is required."
            };
        }

        PhoneNumberLookupResult lookup;
        try
        {
            lookup = await _lookup.LookupAsync(phoneNumber.Trim(), countryCode, cancellationToken);
        }
        catch (System.Exception ex)
        {
            _logger.LogWarning("Phone number lookup failed: {Reason}", PhoneNumberSanitizer.Redact(ex.Message));
            return new ContactNumberRegistrationResult
            {
                Succeeded = false,
                StatusCode = 502,
                Error = "The messaging provider could not validate this number."
            };
        }

        if (!lookup.IsValid || string.IsNullOrWhiteSpace(lookup.CanonicalNumber))
        {
            var reason = lookup.ValidationErrors.Count > 0
                ? string.Join(", ", lookup.ValidationErrors)
                : "The provider does not consider this a usable destination.";
            return new ContactNumberRegistrationResult
            {
                Succeeded = false,
                StatusCode = 400,
                Error = reason
            };
        }

        var existing = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByCanonicalSpecification(buyerId, lookup.CanonicalNumber), cancellationToken);
        if (existing != null)
        {
            return new ContactNumberRegistrationResult
            {
                Succeeded = true,
                StatusCode = 200,
                ContactNumber = existing
            };
        }

        var created = new ShopperContactNumber(buyerId, lookup.CanonicalNumber, lookup.NationalFormat);
        await _contactNumbers.AddAsync(created, cancellationToken);

        return new ContactNumberRegistrationResult
        {
            Succeeded = true,
            StatusCode = 201,
            ContactNumber = created
        };
    }

    public async Task<IReadOnlyList<ShopperContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return numbers;
    }

    public async Task<ContactNumberDeleteResult> DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        var contact = await _contactNumbers.GetByIdAsync(contactNumberId, cancellationToken);
        if (contact == null || contact.BuyerId != buyerId)
        {
            return new ContactNumberDeleteResult { Succeeded = false, StatusCode = 404, Error = "Contact number not found." };
        }

        var scheduled = await _notifications.ListAsync(
            new ScheduledNotificationsByDestinationSpecification(contact.CanonicalNumber), cancellationToken);
        foreach (var notification in scheduled.Where(n => n.BuyerId == buyerId))
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid))
            {
                continue;
            }

            var cancel = await _smsGateway.CancelScheduledAsync(notification.ProviderMessageSid, cancellationToken);
            if (cancel.Succeeded && cancel.Message != null)
            {
                notification.ApplyProviderState(cancel.Message.Status, cancel.Message.ErrorCode, cancel.Message.ErrorMessage);
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            else
            {
                _logger.LogWarning("Could not cancel scheduled notification {NotificationId} after contact number removal: {Reason}",
                    notification.Id, PhoneNumberSanitizer.Redact(cancel.Error));
            }
        }

        await _contactNumbers.DeleteAsync(contact, cancellationToken);
        return new ContactNumberDeleteResult { Succeeded = true, StatusCode = 204 };
    }
}
