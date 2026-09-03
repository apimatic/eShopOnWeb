using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IPhoneNumberLookup _lookup;
    private readonly ISmsGateway _sms;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        IPhoneNumberLookup lookup,
        ISmsGateway sms,
        IAppLogger<ContactNumberService> logger)
    {
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _lookup = lookup;
        _sms = sms;
        _logger = logger;
    }

    public async Task<ContactNumber> RegisterAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw new InvalidContactNumberException("A mobile number is required.");
        }

        var lookup = await _lookup.LookupAsync(phoneNumber.Trim(), cancellationToken);
        if (!lookup.IsUsable || string.IsNullOrWhiteSpace(lookup.CanonicalNumber))
        {
            throw new InvalidContactNumberException(
                lookup.RejectionReason ?? "The provider does not consider this a usable destination.");
        }

        var existing = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndCanonicalSpecification(buyerId, lookup.CanonicalNumber),
            cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var contact = new ContactNumber(buyerId, lookup.CanonicalNumber);
        return await _contactNumbers.AddAsync(contact, cancellationToken);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken)
    {
        return await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken)
    {
        var contact = await _contactNumbers.GetByIdAsync(contactNumberId, cancellationToken);
        if (contact is null || contact.BuyerId != buyerId)
        {
            throw new ContactNumberNotFoundException(contactNumberId);
        }

        var notifications = await _notifications.ListAsync(
            new NotificationsByBuyerSpecification(buyerId),
            cancellationToken);

        foreach (var notification in notifications)
        {
            if (!notification.IsScheduledPending() || notification.ToNumber != contact.CanonicalNumber)
            {
                continue;
            }

            try
            {
                var result = await _sms.CancelScheduledAsync(notification.ProviderSid!, cancellationToken);
                notification.ApplyProviderStatus(result.Status, result.ErrorCode, result.ErrorMessage, result.Body);
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (SmsProviderException)
            {
                _logger.LogWarning("Failed to cancel a scheduled follow-up while removing a contact number for order {OrderId}.", notification.OrderId);
            }
        }

        await _contactNumbers.DeleteAsync(contact, cancellationToken);
    }
}
