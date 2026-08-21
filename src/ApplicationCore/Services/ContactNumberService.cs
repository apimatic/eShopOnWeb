using System.Collections.Generic;
using System.Linq;
using System.Net;
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

    public async Task<ContactNumber> RegisterAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw new UnusableContactNumberException("A mobile number is required.");
        }

        PhoneLookupResult lookup;
        try
        {
            lookup = await _smsGateway.LookupAsync(phoneNumber.Trim(), cancellationToken);
        }
        catch (SmsProviderException ex) when (IsCallerNumberFault(ex))
        {
            throw new UnusableContactNumberException("The provider does not consider this number a usable destination.");
        }

        if (!lookup.IsUsable || string.IsNullOrWhiteSpace(lookup.CanonicalNumber))
        {
            throw new UnusableContactNumberException(
                lookup.RejectionReason ?? "The provider does not consider this number a usable destination.");
        }

        var canonical = lookup.CanonicalNumber;
        var existing = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByCanonicalSpecification(canonical), cancellationToken);

        if (existing is not null)
        {
            if (existing.BuyerId != buyerId)
            {
                throw new UnusableContactNumberException("This number is already registered to another shopper.");
            }

            return existing;
        }

        var contact = new ContactNumber(buyerId, canonical);
        return await _contactNumbers.AddAsync(contact, cancellationToken);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken)
    {
        var list = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return list;
    }

    public async Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken)
    {
        var contact = await _contactNumbers.GetByIdAsync(contactNumberId, cancellationToken);
        if (contact is null || contact.BuyerId != buyerId)
        {
            throw new EntityNotFoundException(nameof(ContactNumber), contactNumberId);
        }

        var pending = await _notifications.ListAsync(
            new ScheduledNotificationsByDestinationSpecification(contact.CanonicalNumber), cancellationToken);

        foreach (var notification in pending.Where(n => n.BuyerId == buyerId && !string.IsNullOrEmpty(n.ProviderSid)))
        {
            try
            {
                var cancelled = await _smsGateway.CancelScheduledAsync(notification.ProviderSid!, cancellationToken);
                notification.ApplyProviderResult(
                    cancelled.Sid ?? notification.ProviderSid,
                    cancelled.Status,
                    cancelled.ErrorCode,
                    cancelled.ErrorMessage,
                    cancelled.DateSent,
                    cancelled.FailureReason);
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (SmsProviderException)
            {
                _logger.LogWarning("Could not cancel a scheduled follow-up while removing a contact number. NotificationId {NotificationId}", notification.Id);
            }
        }

        await _contactNumbers.DeleteAsync(contact, cancellationToken);
    }

    private static bool IsCallerNumberFault(SmsProviderException ex)
    {
        var code = (int?)ex.StatusCode;
        return code is >= 400 and < 500 and not 401 and not 403 and not 429;
    }
}
