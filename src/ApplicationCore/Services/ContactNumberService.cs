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
    private readonly IPhoneNumberLookup _lookup;
    private readonly ISmsGateway _smsGateway;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        IPhoneNumberLookup lookup,
        ISmsGateway smsGateway,
        IAppLogger<ContactNumberService> logger)
    {
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _lookup = lookup;
        _smsGateway = smsGateway;
        _logger = logger;
    }

    public async Task<ContactNumber> RegisterAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw new InvalidContactNumberException("A mobile number is required.");
        }

        var lookup = await _lookup.LookupAsync(phoneNumber.Trim(), cancellationToken);
        if (!lookup.Valid || string.IsNullOrWhiteSpace(lookup.CanonicalNumber))
        {
            var reason = lookup.ValidationErrors.Count > 0
                ? string.Join(", ", lookup.ValidationErrors)
                : "not a usable destination";
            throw new InvalidContactNumberException($"The number is not a usable destination ({reason}).");
        }

        var existing = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndCanonicalSpec(buyerId, lookup.CanonicalNumber), cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var contact = new ContactNumber(buyerId, lookup.CanonicalNumber, lookup.CountryCode);
        return await _contactNumbers.AddAsync(contact, cancellationToken);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpec(buyerId), cancellationToken);
        return numbers;
    }

    public async Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        var contact = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByIdAndBuyerSpec(contactNumberId, buyerId), cancellationToken);
        if (contact is null)
        {
            throw new ContactNumberNotFoundException(contactNumberId);
        }

        var pending = await _notifications.ListAsync(
            new PendingFollowUpsByDestinationSpec(contact.CanonicalNumber), cancellationToken);
        foreach (var notification in pending)
        {
            await CancelPendingAsync(notification, cancellationToken);
        }

        await _contactNumbers.DeleteAsync(contact, cancellationToken);
    }

    private async Task CancelPendingAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(notification.ProviderSid))
        {
            return;
        }

        try
        {
            var snapshot = await _smsGateway.CancelAsync(notification.ProviderSid, cancellationToken);
            if (snapshot is not null)
            {
                notification.ApplyProviderState(snapshot.Sid, snapshot.Status, snapshot.ErrorCode, snapshot.ErrorMessage);
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
        }
        catch (System.Exception ex)
        {
            _logger.LogWarning("Failed to cancel a pending follow-up after a contact number was removed. {ExceptionType}",
                ex.GetType().Name);
        }
    }
}
