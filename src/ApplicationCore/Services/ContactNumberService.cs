using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IPhoneNumberLookupClient _lookupClient;
    private readonly ITwilioMessagingClient _messagingClient;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        IPhoneNumberLookupClient lookupClient,
        ITwilioMessagingClient messagingClient,
        IAppLogger<ContactNumberService> logger)
    {
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _lookupClient = lookupClient;
        _messagingClient = messagingClient;
        _logger = logger;
    }

    public async Task<ContactNumber> RegisterAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw new InvalidContactNumberException();
        }

        PhoneNumberLookupResult lookup;
        try
        {
            lookup = await _lookupClient.LookupAsync(phoneNumber.Trim(), cancellationToken);
        }
        catch
        {
            throw new InvalidContactNumberException();
        }

        if (!lookup.IsValid || string.IsNullOrWhiteSpace(lookup.CanonicalPhoneNumber))
        {
            throw new InvalidContactNumberException();
        }

        var canonical = lookup.CanonicalPhoneNumber;
        var existing = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndCanonicalSpec(buyerId, canonical), cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var contactNumber = new ContactNumber(buyerId, canonical);
        return await _contactNumbers.AddAsync(contactNumber, cancellationToken);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpec(buyerId), cancellationToken);
        return numbers;
    }

    public async Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        var contactNumber = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByIdAndBuyerSpec(contactNumberId, buyerId), cancellationToken);
        if (contactNumber is null)
        {
            return;
        }

        var pending = await _notifications.ListAsync(
            new PendingNotificationsByContactNumberIdSpec(contactNumberId), cancellationToken);
        foreach (var notification in pending.Where(n => n.IsPendingWithProvider && n.HasProviderIdentity))
        {
            try
            {
                var updated = await _messagingClient.CancelMessageAsync(notification.ProviderMessageSid!, cancellationToken);
                if (updated is not null)
                {
                    notification.RecordProviderResult(updated.Sid, updated.Status, updated.ErrorCode, updated.ErrorMessage);
                    await _notifications.UpdateAsync(notification, cancellationToken);
                }
            }
            catch
            {
                _logger.LogWarning("Failed to cancel provider message for notification {NotificationId} while removing a contact number.", notification.Id);
            }
        }

        await _contactNumbers.DeleteAsync(contactNumber, cancellationToken);
    }
}
