using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
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
    private readonly ITwilioLookupClient _lookupClient;
    private readonly ITwilioMessagingClient _messagingClient;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        ITwilioLookupClient lookupClient,
        ITwilioMessagingClient messagingClient,
        IAppLogger<ContactNumberService> logger)
    {
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _lookupClient = lookupClient;
        _messagingClient = messagingClient;
        _logger = logger;
    }

    public async Task<ContactNumber> RegisterAsync(string buyerId, string phoneNumber, string? countryCode, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(phoneNumber, nameof(phoneNumber));

        var lookup = await _lookupClient.LookupAsync(phoneNumber, countryCode, cancellationToken);
        if (!lookup.Valid || string.IsNullOrEmpty(lookup.CanonicalNumber))
        {
            var reason = lookup.ValidationErrors.Count > 0
                ? string.Join(", ", lookup.ValidationErrors)
                : "NOT_A_USABLE_DESTINATION";
            throw new InvalidContactNumberException($"The provider does not consider this a usable destination ({reason}).");
        }

        var existing = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndCanonicalSpecification(buyerId, lookup.CanonicalNumber),
            cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var contactNumber = new ContactNumber(buyerId, lookup.CanonicalNumber);
        return await _contactNumbers.AddAsync(contactNumber, cancellationToken);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return numbers;
    }

    public async Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var contactNumber = await _contactNumbers.GetByIdAsync(contactNumberId, cancellationToken);
        if (contactNumber is null || contactNumber.BuyerId != buyerId)
        {
            throw new KeyNotFoundException("Contact number was not found.");
        }

        var scheduled = await _notifications.ListAsync(
            new ScheduledNotificationsToNumberSpecification(contactNumber.CanonicalNumber),
            cancellationToken);

        foreach (var notification in scheduled.Where(n => n.BuyerId == buyerId))
        {
            try
            {
                var updated = await _messagingClient.CancelAsync(notification.ProviderMessageSid!, cancellationToken);
                notification.ApplyProviderState(updated.Status ?? "canceled", updated.ErrorCode);
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to cancel a scheduled notification {NotificationId} after contact number removal: {Message}", notification.Id, ex.Message);
            }
        }

        await _contactNumbers.DeleteAsync(contactNumber, cancellationToken);
    }

    public async Task<ContactNumber?> GetPreferredForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var numbers = await ListForBuyerAsync(buyerId, cancellationToken);
        return numbers.FirstOrDefault();
    }

    public async Task<bool> IsStillRegisteredAsync(string buyerId, string canonicalNumber, CancellationToken cancellationToken = default)
    {
        var existing = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndCanonicalSpecification(buyerId, canonicalNumber),
            cancellationToken);
        return existing is not null;
    }
}
