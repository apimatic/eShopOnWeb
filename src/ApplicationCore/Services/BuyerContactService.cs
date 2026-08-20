using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Sms;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class BuyerContactService : IBuyerContactService
{
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly ISmsNotificationGateway _smsGateway;
    private readonly IAppLogger<BuyerContactService> _logger;

    public BuyerContactService(
        IRepository<ContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        ISmsNotificationGateway smsGateway,
        IAppLogger<BuyerContactService> logger)
    {
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _smsGateway = smsGateway;
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
            lookup = await _smsGateway.LookupAsync(phoneNumber.Trim(), cancellationToken);
        }
        catch (System.Exception ex)
        {
            _logger.LogWarning("Phone number lookup failed.");
            throw new InvalidContactNumberException("The number could not be verified as a usable destination.");
        }

        if (!lookup.Valid || string.IsNullOrWhiteSpace(lookup.PhoneNumber))
        {
            throw new InvalidContactNumberException(
                "The number is not a usable destination.",
                lookup.ValidationErrors);
        }

        var canonical = lookup.PhoneNumber;
        var existing = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndPhoneSpecification(buyerId, canonical), cancellationToken);
        if (existing is not null)
        {
            throw new DuplicateException("This number is already on file for the current shopper.");
        }

        var contact = new ContactNumber(buyerId, canonical, lookup.NationalFormat ?? canonical);
        return await _contactNumbers.AddAsync(contact, cancellationToken);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return numbers;
    }

    public async Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        var contact = await _contactNumbers.GetByIdAsync(contactNumberId, cancellationToken);
        if (contact is null || !contact.BelongsTo(buyerId))
        {
            throw new EntityNotFoundException("Contact number was not found.");
        }

        var scheduled = await _notifications.ListAsync(
            new ScheduledFollowUpsByContactNumberSpecification(contactNumberId), cancellationToken);
        foreach (var notification in scheduled.Where(n => n.IsScheduledPending()))
        {
            try
            {
                var updated = await _smsGateway.CancelScheduledAsync(notification.ProviderMessageSid!, cancellationToken);
                if (updated is not null)
                {
                    notification.ApplyProviderSnapshot(updated.Status ?? "canceled", updated.ErrorCode, updated.Body);
                    await _notifications.UpdateAsync(notification, cancellationToken);
                }
            }
            catch (System.Exception)
            {
                _logger.LogWarning(
                    "Failed to cancel scheduled notification {NotificationId} after contact number removal.",
                    notification.Id);
            }
        }

        await _contactNumbers.DeleteAsync(contact, cancellationToken);
    }
}
