using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IPhoneNumberLookupService _lookup;
    private readonly ISmsMessagingService _messaging;
    private readonly ILogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        IPhoneNumberLookupService lookup,
        ISmsMessagingService messaging,
        ILogger<ContactNumberService> logger)
    {
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _lookup = lookup;
        _messaging = messaging;
        _logger = logger;
    }

    public async Task<ContactNumber> RegisterAsync(string buyerId, string phoneNumber, string? countryCode, CancellationToken cancellationToken = default)
    {
        var lookup = await _lookup.LookupAsync(phoneNumber, countryCode, cancellationToken);
        if (!lookup.Valid || string.IsNullOrEmpty(lookup.PhoneNumber))
        {
            var reasons = lookup.ValidationErrors.Length == 0
                ? "the number is not a usable destination"
                : string.Join(", ", lookup.ValidationErrors);
            throw new InvalidContactNumberException($"The mobile number was rejected: {reasons}.");
        }

        var existing = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndPhoneSpecification(buyerId, lookup.PhoneNumber), cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var contact = new ContactNumber(buyerId, lookup.PhoneNumber, lookup.NationalFormat);
        return await _contactNumbers.AddAsync(contact, cancellationToken);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var list = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return list;
    }

    public async Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        var contact = await _contactNumbers.GetByIdAsync(contactNumberId, cancellationToken);
        if (contact is null || contact.BuyerId != buyerId)
        {
            throw new ContactNumberNotFoundException(contactNumberId);
        }

        var scheduled = await _notifications.ListAsync(
            new ScheduledFollowUpsByContactSpecification(contactNumberId), cancellationToken);

        foreach (var notification in scheduled)
        {
            try
            {
                var updated = await _messaging.CancelAsync(notification.ProviderMessageSid!, cancellationToken);
                if (updated is not null)
                {
                    notification.SyncFromProvider(updated.Status ?? "canceled", updated.ErrorCode, updated.Body);
                }
                else
                {
                    notification.RecordProviderFailure("canceled", null);
                }
            }
            catch (TwilioUnavailableException ex)
            {
                _logger.LogWarning(ex, "Failed to cancel a scheduled follow-up while removing a contact number. NotificationId {NotificationId}.", notification.Id);
            }

            notification.ClearContactNumber();
            await _notifications.UpdateAsync(notification, cancellationToken);
        }

        await _contactNumbers.DeleteAsync(contact, cancellationToken);
    }
}
