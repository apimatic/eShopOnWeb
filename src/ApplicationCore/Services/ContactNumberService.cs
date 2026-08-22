using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly ITwilioLookupClient _lookupClient;
    private readonly IOrderNotificationService _notifications;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ContactNumber> contactNumbers,
        ITwilioLookupClient lookupClient,
        IOrderNotificationService notifications,
        IAppLogger<ContactNumberService> logger)
    {
        _contactNumbers = contactNumbers;
        _lookupClient = lookupClient;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task<ContactNumber> RegisterAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw new InvalidContactNumberException("A phone number is required.");
        }

        var lookup = await _lookupClient.LookupAsync(phoneNumber.Trim(), cancellationToken);
        if (!lookup.Valid || string.IsNullOrWhiteSpace(lookup.CanonicalPhoneNumber))
        {
            var reason = lookup.ValidationErrors.Count > 0
                ? string.Join(", ", lookup.ValidationErrors)
                : "not a usable destination";
            throw new InvalidContactNumberException($"The number is not a usable destination ({reason}).");
        }

        var existing = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndPhoneSpec(buyerId, lookup.CanonicalPhoneNumber), cancellationToken);
        if (existing != null)
        {
            throw new DuplicateException("This contact number is already registered.");
        }

        var contact = new ContactNumber(buyerId, lookup.CanonicalPhoneNumber);
        await _contactNumbers.AddAsync(contact, cancellationToken);
        _logger.LogInformation("Registered a contact number for buyer {BuyerId} as contact {ContactNumberId}.", buyerId, contact.Id);
        return contact;
    }

    public async Task<IReadOnlyList<ContactNumber>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerIdSpec(buyerId), cancellationToken);
        return numbers;
    }

    public async Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        var contact = await _contactNumbers.GetByIdAsync(contactNumberId, cancellationToken);
        if (contact == null || contact.BuyerId != buyerId)
        {
            throw new KeyNotFoundException("Contact number not found.");
        }

        var destination = contact.CanonicalPhoneNumber;
        await _contactNumbers.DeleteAsync(contact, cancellationToken);
        await _notifications.CancelScheduledForDestinationAsync(destination, cancellationToken);
        _logger.LogInformation("Removed contact {ContactNumberId} for buyer {BuyerId}.", contactNumberId, buyerId);
    }
}
