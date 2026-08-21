using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly ITwilioLookupClient _lookupClient;
    private readonly ITwilioMessagingClient _messagingClient;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        ITwilioLookupClient lookupClient,
        ITwilioMessagingClient messagingClient,
        IRepository<ContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        IAppLogger<ContactNumberService> logger)
    {
        _lookupClient = lookupClient;
        _messagingClient = messagingClient;
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task<ContactNumber> RegisterAsync(string buyerId, string phoneNumber, string? countryCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw new InvalidContactNumberException("A phone number is required.");
        }

        var lookup = await _lookupClient.LookupAsync(phoneNumber.Trim(), countryCode, cancellationToken);
        if (!lookup.Valid || string.IsNullOrWhiteSpace(lookup.PhoneNumber))
        {
            var reason = lookup.ValidationErrors.Count == 0
                ? "The provider does not consider this a usable destination."
                : "The provider does not consider this a usable destination: " + string.Join(", ", lookup.ValidationErrors);
            throw new InvalidContactNumberException(reason, lookup.ValidationErrors);
        }

        var existing = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndPhoneSpecification(buyerId, lookup.PhoneNumber), cancellationToken);
        if (existing is not null)
        {
            throw new DuplicateException("That contact number is already registered.");
        }

        var contact = new ContactNumber(buyerId, lookup.PhoneNumber, lookup.NationalFormat, lookup.CountryCode);
        return await _contactNumbers.AddAsync(contact, cancellationToken);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return numbers;
    }

    public async Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        var existing = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByIdAndBuyerSpecification(contactNumberId, buyerId), cancellationToken);
        if (existing is null)
        {
            throw new KeyNotFoundException("Contact number was not found.");
        }

        await CancelPendingToDestinationAsync(existing.PhoneNumber, cancellationToken);
        await _contactNumbers.DeleteAsync(existing, cancellationToken);
    }

    private async Task CancelPendingToDestinationAsync(string destinationNumber, CancellationToken cancellationToken)
    {
        var pending = await _notifications.ListAsync(new NotificationsToDestinationSpecification(destinationNumber), cancellationToken);
        foreach (var notification in pending)
        {
            await TryCancelProviderMessageAsync(notification, cancellationToken);
        }
    }

    private async Task TryCancelProviderMessageAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            return;
        }

        try
        {
            var snapshot = await _messagingClient.FetchAsync(notification.ProviderMessageSid, cancellationToken);
            if (snapshot?.Status is not null)
            {
                notification.ApplyProviderState(snapshot.Status, snapshot.ErrorCode, snapshot.Sid);
            }

            if (!notification.IsPendingFollowUp()
                && !string.Equals(notification.ProviderStatus, "scheduled", StringComparison.OrdinalIgnoreCase))
            {
                await _notifications.UpdateAsync(notification, cancellationToken);
                return;
            }

            var cancelled = await _messagingClient.CancelAsync(notification.ProviderMessageSid, cancellationToken);
            if (cancelled?.Status is not null)
            {
                notification.ApplyProviderState(cancelled.Status, cancelled.ErrorCode, cancelled.Sid);
            }

            await _notifications.UpdateAsync(notification, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to cancel provider message {MessageSid} for notification {NotificationId}.", notification.ProviderMessageSid, notification.Id);
        }
    }
}
