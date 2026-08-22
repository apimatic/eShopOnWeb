using System;
using System.Collections.Generic;
using System.Linq;
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
    private static readonly HashSet<string> UnusableLineTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "landline",
        "voicemail",
        "pager",
        "premium",
        "sharedCost",
        "uan",
        "tollFree"
    };

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
            throw new InvalidContactNumberException("A phone number is required.");
        }

        var lookup = await _lookup.LookupAsync(phoneNumber, cancellationToken);
        if (!lookup.Valid || string.IsNullOrWhiteSpace(lookup.CanonicalE164))
        {
            var reason = lookup.ValidationErrors.Count > 0
                ? string.Join(", ", lookup.ValidationErrors)
                : "the provider does not consider this a usable destination";
            throw new InvalidContactNumberException($"That number cannot be registered: {reason}.");
        }

        if (lookup.LineTypeErrorCode is null
            && !string.IsNullOrWhiteSpace(lookup.LineType)
            && UnusableLineTypes.Contains(lookup.LineType))
        {
            throw new InvalidContactNumberException("That number is not a usable SMS destination.");
        }

        var existing = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndPhoneSpecification(buyerId, lookup.CanonicalE164), cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var contact = new ContactNumber(
            buyerId,
            lookup.CanonicalE164,
            lookup.NationalFormat,
            lookup.CountryCode,
            lookup.LineType);

        return await _contactNumbers.AddAsync(contact, cancellationToken);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return numbers;
    }

    public async Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        var contact = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndIdSpecification(buyerId, contactNumberId), cancellationToken);
        if (contact is null)
        {
            throw new EntityNotFoundException("Contact number not found.");
        }

        var destination = contact.PhoneNumber;
        await _contactNumbers.DeleteAsync(contact, cancellationToken);

        var outstanding = await _notifications.ListAsync(
            new OutstandingFollowUpsByDestinationSpecification(buyerId, destination), cancellationToken);
        foreach (var followUp in outstanding)
        {
            try
            {
                var cancelled = await _smsGateway.CancelAsync(followUp.ProviderMessageSid!, cancellationToken);
                if (cancelled is not null)
                {
                    followUp.ApplyProviderSnapshot(cancelled.Status, cancelled.ErrorCode, cancelled.Body);
                    await _notifications.UpdateAsync(followUp, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to cancel scheduled follow-up {NotificationId} after contact number removal.", followUp.Id);
            }
        }
    }
}
