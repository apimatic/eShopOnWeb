using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IPhoneNumberLookup _lookup;
    private readonly IOrderNotificationService _notifications;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ContactNumber> contactNumbers,
        IPhoneNumberLookup lookup,
        IOrderNotificationService notifications,
        IAppLogger<ContactNumberService> logger)
    {
        _contactNumbers = contactNumbers;
        _lookup = lookup;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task<ContactNumber> RegisterAsync(string buyerId, string rawNumber, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawNumber))
        {
            throw new UnusableContactNumberException("A mobile number is required.");
        }

        var lookup = await _lookup.LookupAsync(rawNumber.Trim(), cancellationToken);
        if (!lookup.ProviderCallSucceeded)
        {
            throw new MessagingProviderException(lookup.FailureMessage ?? "The messaging provider is unavailable.");
        }

        if (!lookup.IsUsable || string.IsNullOrWhiteSpace(lookup.CanonicalNumber))
        {
            throw new UnusableContactNumberException(
                lookup.FailureMessage ?? "The provider does not consider this a usable destination.");
        }

        var existing = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndCanonicalSpec(buyerId, lookup.CanonicalNumber), cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var contact = new ContactNumber(buyerId, lookup.CanonicalNumber);
        await _contactNumbers.AddAsync(contact, cancellationToken);
        _logger.LogInformation("Registered contact number {ContactNumberId} for buyer {BuyerId}", contact.Id, buyerId);
        return contact;
    }

    public async Task<IReadOnlyList<ContactNumber>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken)
    {
        return await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpec(buyerId), cancellationToken);
    }

    public async Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken)
    {
        var contact = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByIdAndBuyerSpec(contactNumberId, buyerId), cancellationToken);
        if (contact is null)
        {
            throw new KeyNotFoundException("Contact number not found.");
        }

        var canonical = contact.CanonicalNumber;
        await _notifications.CancelPendingFollowUpsForNumberAsync(buyerId, canonical, cancellationToken);
        await _contactNumbers.DeleteAsync(contact, cancellationToken);
        _logger.LogInformation("Deleted contact number {ContactNumberId} for buyer {BuyerId}", contactNumberId, buyerId);
    }
}
