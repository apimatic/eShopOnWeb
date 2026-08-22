using System;
using System.Collections.Generic;
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
    private readonly ISmsGateway _smsGateway;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        ISmsGateway smsGateway,
        IAppLogger<ContactNumberService> logger)
    {
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _smsGateway = smsGateway;
        _logger = logger;
    }

    public async Task<ContactNumber> RegisterAsync(string buyerId, string phoneNumber, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw new InvalidContactNumberException("A phone number is required.");
        }

        PhoneLookupResult lookup;
        try
        {
            lookup = await _smsGateway.LookupAsync(phoneNumber, ct);
        }
        catch (SmsGatewayException ex) when ((int?)ex.StatusCode is >= 400 and < 500 and not 401 and not 403)
        {
            throw new InvalidContactNumberException("The phone number is not a usable destination.");
        }

        if (!lookup.Valid || string.IsNullOrWhiteSpace(lookup.CanonicalNumber))
        {
            throw new InvalidContactNumberException("The phone number is not a usable destination.");
        }

        var existing = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndCanonicalSpecification(buyerId, lookup.CanonicalNumber), ct);
        if (existing is not null)
        {
            return existing;
        }

        var contact = new ContactNumber(buyerId, lookup.CanonicalNumber);
        return await _contactNumbers.AddAsync(contact, ct);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListForBuyerAsync(string buyerId, CancellationToken ct)
    {
        return await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), ct);
    }

    public async Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken ct)
    {
        var contact = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByIdAndBuyerSpecification(contactNumberId, buyerId), ct);
        if (contact is null)
        {
            throw new KeyNotFoundException("Contact number not found.");
        }

        var scheduled = await _notifications.ListAsync(
            new ScheduledFollowUpsByContactNumberSpecification(contact.Id), ct);
        foreach (var notification in scheduled)
        {
            if (!notification.IsScheduledFollowUp() || string.IsNullOrEmpty(notification.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var snapshot = await _smsGateway.CancelScheduledAsync(notification.ProviderMessageSid, ct);
                notification.SyncFromProvider(snapshot.Status, snapshot.ErrorCode, snapshot.ErrorMessage, snapshot.Body);
                await _notifications.UpdateAsync(notification, ct);
            }
            catch (SmsGatewayException)
            {
                _logger.LogWarning(
                    "Failed to cancel scheduled follow-up {NotificationId} (sid present) when deleting contact number {ContactNumberId}.",
                    notification.Id,
                    contactNumberId);
            }
        }

        await _contactNumbers.DeleteAsync(contact, ct);
    }
}
