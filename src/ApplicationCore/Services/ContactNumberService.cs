using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Extensions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IPhoneNumberLookupService _lookup;
    private readonly ISmsGateway _smsGateway;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ContactNumber> contactNumbers,
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

    public async Task<ContactNumber> RegisterAsync(string buyerId, string rawPhoneNumber, string? countryCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawPhoneNumber))
        {
            throw new InvalidPhoneNumberException("A phone number is required.");
        }

        var lookup = await _lookup.LookupAsync(rawPhoneNumber.Trim(), countryCode, cancellationToken);
        if (!lookup.Valid || string.IsNullOrWhiteSpace(lookup.PhoneNumber))
        {
            var errors = lookup.ValidationErrors.Count > 0
                ? lookup.ValidationErrors
                : new[] { "NOT_A_USABLE_DESTINATION" };
            throw new InvalidPhoneNumberException(
                "The provider does not consider this number a usable destination.",
                errors);
        }

        var existing = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndPhoneSpecification(buyerId, lookup.PhoneNumber),
            cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        var contact = new ContactNumber(buyerId, lookup.PhoneNumber, lookup.NationalFormat);
        return await _contactNumbers.AddAsync(contact, cancellationToken);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var list = await _contactNumbers.ListAsync(new ContactNumbersByBuyerIdSpecification(buyerId), cancellationToken);
        return list;
    }

    public async Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        var contact = await _contactNumbers.GetByIdAsync(contactNumberId, cancellationToken);
        if (contact == null || contact.BuyerId != buyerId)
        {
            throw new ResourceNotFoundException("Contact number not found.");
        }

        await CancelOutstandingFollowUpsAsync(contact, cancellationToken);
        await _contactNumbers.DeleteAsync(contact, cancellationToken);
    }

    public async Task<ContactNumber?> GetPrimaryForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerIdSpecification(buyerId), cancellationToken);
        return numbers.FirstOrDefault();
    }

    public async Task<bool> IsRegisteredAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken = default)
    {
        var existing = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndPhoneSpecification(buyerId, phoneNumber),
            cancellationToken);
        return existing != null;
    }

    private async Task CancelOutstandingFollowUpsAsync(ContactNumber contact, CancellationToken cancellationToken)
    {
        var pending = await _notifications.ListAsync(
            new ScheduledNotificationsByDestinationSpecification(contact.BuyerId, contact.PhoneNumber),
            cancellationToken);

        foreach (var notification in pending)
        {
            if (string.IsNullOrEmpty(notification.ProviderSid))
            {
                continue;
            }

            try
            {
                var snapshot = await _smsGateway.FetchAsync(notification.ProviderSid, cancellationToken);
                if (snapshot != null && IsCancellable(snapshot.Status))
                {
                    snapshot = await _smsGateway.CancelScheduledAsync(notification.ProviderSid, cancellationToken)
                               ?? snapshot;
                }

                if (snapshot != null)
                {
                    notification.ApplyProviderSnapshot(snapshot.Status, snapshot.ErrorCode, snapshot.Body, snapshot.DateCreated, snapshot.DateSent);
                    await _notifications.UpdateAsync(notification, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Failed to cancel scheduled notification {NotificationId} after contact number removal: {Message}",
                    notification.Id,
                    PhoneNumberLogSanitizer.Redact(ex.Message));
            }
        }
    }

    private static bool IsCancellable(string status) =>
        string.Equals(status, "scheduled", StringComparison.OrdinalIgnoreCase);
}
