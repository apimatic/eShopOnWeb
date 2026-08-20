using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly ITwilioLookupsClient _lookups;
    private readonly ITwilioMessagingClient _messaging;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        ITwilioLookupsClient lookups,
        ITwilioMessagingClient messaging,
        IAppLogger<ContactNumberService> logger)
    {
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _lookups = lookups;
        _messaging = messaging;
        _logger = logger;
    }

    public async Task<ContactNumber> RegisterAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw new InvalidContactNumberException("A phone number is required.");
        }

        PhoneNumberLookupResult lookup;
        try
        {
            lookup = await _lookups.LookupAsync(phoneNumber.Trim(), cancellationToken);
        }
        catch
        {
            throw new InvalidContactNumberException("The provider could not validate this number as a usable destination.");
        }

        if (!lookup.IsValid || string.IsNullOrWhiteSpace(lookup.CanonicalNumber))
        {
            throw new InvalidContactNumberException("The provider does not consider this number a usable destination.");
        }

        var existing = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndCanonicalSpecification(buyerId, lookup.CanonicalNumber),
            cancellationToken);
        if (existing is not null)
        {
            throw new DuplicateException("This number is already registered.");
        }

        var contact = new ContactNumber(buyerId, lookup.CanonicalNumber);
        return await _contactNumbers.AddAsync(contact, cancellationToken);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var list = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return list;
    }

    public async Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        var contact = await _contactNumbers.FirstOrDefaultAsync(new ContactNumberByIdSpecification(contactNumberId), cancellationToken);
        if (contact is null || contact.BuyerId != buyerId || contact.IsDeleted)
        {
            throw new NotFoundException("Contact number was not found.");
        }

        contact.MarkDeleted();
        await _contactNumbers.UpdateAsync(contact, cancellationToken);

        var pending = await _notifications.ListAsync(new OrderNotificationsByBuyerSpecification(buyerId), cancellationToken);
        foreach (var notification in pending)
        {
            if (notification.ContactNumberId != contact.Id || !notification.IsCancellableFollowUp)
            {
                continue;
            }

            try
            {
                var updated = await _messaging.UpdateAsync(notification.ProviderMessageSid!, body: null, status: "canceled", cancellationToken);
                notification.ApplyProviderState(updated.Status ?? "canceled", updated.ErrorCode, updated.ErrorMessage, updated.Body);
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch
            {
                _logger.LogWarning("Could not cancel a queued follow-up after a contact number was removed. Notification {NotificationId}.", notification.Id);
            }
        }
    }
}
