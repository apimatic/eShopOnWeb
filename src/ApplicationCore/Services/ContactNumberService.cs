using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private static readonly HashSet<string> UnusableLineTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "landline",
        "voicemail",
        "pager"
    };

    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly ITwilioLookupClient _lookup;
    private readonly IOrderNotificationService _notifications;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ContactNumber> contactNumbers,
        ITwilioLookupClient lookup,
        IOrderNotificationService notifications,
        IAppLogger<ContactNumberService> logger)
    {
        _contactNumbers = contactNumbers;
        _lookup = lookup;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task<ContactNumber> RegisterAsync(string buyerId, string phoneNumber, string? countryCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw new InvalidContactNumberException(new[] { "NOT_A_NUMBER" });
        }

        var lookup = await _lookup.LookupAsync(phoneNumber.Trim(), countryCode, cancellationToken);
        if (!lookup.Valid || string.IsNullOrWhiteSpace(lookup.CanonicalPhoneNumber))
        {
            var reasons = lookup.ValidationErrors.Count > 0
                ? lookup.ValidationErrors
                : new[] { "INVALID" };
            throw new InvalidContactNumberException(reasons);
        }

        if (!string.IsNullOrEmpty(lookup.LineType) && UnusableLineTypes.Contains(lookup.LineType))
        {
            throw new InvalidContactNumberException(new[] { "NOT_SMS_CAPABLE" });
        }

        var canonical = lookup.CanonicalPhoneNumber;
        var existing = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndPhoneSpec(buyerId, canonical), cancellationToken);

        if (existing is not null)
        {
            if (!existing.IsDeleted)
            {
                throw new DuplicateException("This mobile number is already registered.");
            }

            existing.Restore(lookup.NationalFormat, lookup.LineType);
            await _contactNumbers.UpdateAsync(existing, cancellationToken);
            _logger.LogInformation("Restored a previously removed contact number for buyer {BuyerId}.", buyerId);
            return existing;
        }

        var contact = new ContactNumber(buyerId, canonical, lookup.NationalFormat, lookup.LineType);
        await _contactNumbers.AddAsync(contact, cancellationToken);
        _logger.LogInformation("Registered a contact number for buyer {BuyerId}.", buyerId);
        return contact;
    }

    public async Task<IReadOnlyList<ContactNumber>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var list = await _contactNumbers.ListAsync(new ActiveContactNumbersByBuyerSpec(buyerId), cancellationToken);
        return list;
    }

    public async Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        var contact = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndIdSpec(buyerId, contactNumberId), cancellationToken);
        if (contact is null || contact.IsDeleted)
        {
            throw new KeyNotFoundException("Contact number not found.");
        }

        contact.Delete();
        await _contactNumbers.UpdateAsync(contact, cancellationToken);
        await _notifications.CancelScheduledForContactAsync(contactNumberId, cancellationToken);
        _logger.LogInformation("Removed contact number {ContactNumberId} for buyer {BuyerId}.", contactNumberId, buyerId);
    }
}
