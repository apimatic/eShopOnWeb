using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ShopperContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IPhoneNumberLookupService _lookup;
    private readonly ISmsMessageGateway _smsGateway;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ShopperContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        IPhoneNumberLookupService lookup,
        ISmsMessageGateway smsGateway,
        IAppLogger<ContactNumberService> logger)
    {
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _lookup = lookup;
        _smsGateway = smsGateway;
        _logger = logger;
    }

    public async Task<ShopperContactNumber> RegisterAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw new InvalidContactNumberException("A phone number is required.");
        }

        PhoneNumberLookupResult lookup;
        try
        {
            lookup = await _lookup.LookupAsync(phoneNumber.Trim(), cancellationToken);
        }
        catch (SmsGatewayException)
        {
            throw new InvalidContactNumberException("The provider could not validate this number as a destination.");
        }

        if (!lookup.Valid || string.IsNullOrWhiteSpace(lookup.CanonicalPhoneNumber))
        {
            var reason = lookup.ValidationErrors.Count > 0
                ? string.Join(", ", lookup.ValidationErrors)
                : "not a usable destination";
            throw new InvalidContactNumberException($"The provider rejected this number ({reason}).");
        }

        var existing = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndCanonicalSpec(buyerId, lookup.CanonicalPhoneNumber),
            cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var contact = new ShopperContactNumber(buyerId, lookup.CanonicalPhoneNumber);
        return await _contactNumbers.AddAsync(contact, cancellationToken);
    }

    public async Task<IReadOnlyList<ShopperContactNumber>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpec(buyerId), cancellationToken);
        return numbers;
    }

    public async Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        var contact = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndIdSpec(buyerId, contactNumberId),
            cancellationToken);
        if (contact is null)
        {
            return false;
        }

        var scheduled = await _notifications.ListAsync(
            new ScheduledNotificationsByContactNumberSpec(contact.Id),
            cancellationToken);
        foreach (var notification in scheduled)
        {
            if (string.IsNullOrWhiteSpace(notification.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var cancelled = await _smsGateway.CancelAsync(notification.ProviderMessageSid, cancellationToken);
                notification.SyncFromProvider(cancelled.Status, cancelled.ErrorCode, cancelled.ErrorMessage);
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception)
            {
                _logger.LogWarning(
                    "Failed to cancel a queued notification {NotificationId} after contact {ContactNumberId} was removed.",
                    notification.Id,
                    contactNumberId);
            }
        }

        await _contactNumbers.DeleteAsync(contact, cancellationToken);
        return true;
    }
}
