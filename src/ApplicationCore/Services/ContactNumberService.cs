using System;
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
    private readonly IRepository<ShopperContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly ISmsGateway _smsGateway;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ShopperContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        ISmsGateway smsGateway,
        IAppLogger<ContactNumberService> logger)
    {
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _smsGateway = smsGateway;
        _logger = logger;
    }

    public async Task<ShopperContactNumber> RegisterAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw new InvalidContactNumberException("A mobile number is required.");
        }

        var lookup = await _smsGateway.LookupAsync(phoneNumber.Trim(), cancellationToken);
        if (!lookup.IsUsable || string.IsNullOrWhiteSpace(lookup.CanonicalNumber))
        {
            throw new InvalidContactNumberException(lookup.RejectionReason ?? "The number is not a usable destination.");
        }

        var existingCanonical = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByCanonicalSpecification(lookup.CanonicalNumber), cancellationToken);
        if (existingCanonical != null)
        {
            if (existingCanonical.BuyerId != buyerId)
            {
                throw new InvalidContactNumberException("The number is not a usable destination.");
            }

            throw new DuplicateException("This number is already registered.");
        }

        var contact = new ShopperContactNumber(buyerId, lookup.CanonicalNumber);
        return await _contactNumbers.AddAsync(contact, cancellationToken);
    }

    public async Task<IReadOnlyList<ShopperContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken)
    {
        var list = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return list;
    }

    public async Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken)
    {
        var contact = await _contactNumbers.GetByIdAsync(contactNumberId, cancellationToken);
        if (contact is null || contact.BuyerId != buyerId)
        {
            throw new ContactNumberNotFoundException(contactNumberId);
        }

        var pendingFollowUps = await _notifications.ListAsync(
            new ActiveNotificationsByContactNumberSpecification(contactNumberId), cancellationToken);
        foreach (var followUp in pendingFollowUps.Where(n => n.IsCancellableSchedule()))
        {
            var cancel = await _smsGateway.CancelScheduledAsync(followUp.ProviderSid!, cancellationToken);
            followUp.ApplyProviderSnapshot(cancel.Status ?? "canceled", cancel.ErrorCode, cancel.ErrorMessage);
            await _notifications.UpdateAsync(followUp, cancellationToken);
        }

        await _contactNumbers.DeleteAsync(contact, cancellationToken);
        _logger.LogInformation("Removed contact number {ContactNumberId} for the calling shopper.", contactNumberId);
    }
}
