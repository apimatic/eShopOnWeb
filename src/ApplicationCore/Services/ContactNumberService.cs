using System;
using System.Collections.Generic;
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
    private readonly IRepository<ShopperContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly ITwilioLookupClient _lookupClient;
    private readonly ITwilioMessagingClient _messagingClient;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ShopperContactNumber> contactNumbers,
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

    public async Task<ShopperContactNumber> RegisterAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw new InvalidContactNumberException("A mobile number is required.");
        }

        PhoneNumberLookupResult lookup;
        try
        {
            lookup = await _lookupClient.LookupAsync(phoneNumber.Trim(), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Phone number lookup failed for buyer {BuyerId}: {Message}", buyerId, PiiRedactor.Redact(ex.Message));
            throw new InvalidContactNumberException("The number could not be validated with the messaging provider.");
        }

        if (!lookup.Valid || string.IsNullOrWhiteSpace(lookup.CanonicalPhoneNumber))
        {
            var reason = lookup.ValidationErrors.Count > 0
                ? string.Join(", ", lookup.ValidationErrors)
                : "not a usable destination";
            throw new InvalidContactNumberException($"The number is not a usable destination ({reason}).");
        }

        var existing = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndCanonicalSpec(buyerId, lookup.CanonicalPhoneNumber), cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        var contact = new ShopperContactNumber(buyerId, lookup.CanonicalPhoneNumber);
        await _contactNumbers.AddAsync(contact, cancellationToken);
        _logger.LogInformation("Registered contact number {ContactNumberId} for buyer {BuyerId}", contact.Id, buyerId);
        return contact;
    }

    public async Task<IReadOnlyList<ShopperContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpec(buyerId), cancellationToken);
        return numbers;
    }

    public async Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        var contact = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndIdSpec(buyerId, contactNumberId), cancellationToken);
        if (contact is null)
        {
            throw new KeyNotFoundException("Contact number was not found.");
        }

        var destination = contact.CanonicalNumber;
        var scheduled = await _notifications.ListAsync(
            new ScheduledNotificationsByDestinationSpec(destination), cancellationToken);

        foreach (var notification in scheduled)
        {
            await TryCancelProviderMessageAsync(notification, cancellationToken);
        }

        await _contactNumbers.DeleteAsync(contact, cancellationToken);
        _logger.LogInformation("Removed contact number {ContactNumberId} for buyer {BuyerId}", contactNumberId, buyerId);
    }

    private async Task TryCancelProviderMessageAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            return;
        }

        try
        {
            var updated = await _messagingClient.UpdateMessageAsync(
                notification.ProviderMessageSid,
                new TwilioUpdateMessageRequest { Status = "canceled" },
                cancellationToken);
            notification.ApplyProviderState(updated.Status ?? "canceled", updated.ErrorCode, updated.ErrorMessage, updated.Body);
            await _notifications.UpdateAsync(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Failed to cancel provider message for notification {NotificationId}: {Message}",
                notification.Id,
                PiiRedactor.Redact(ex.Message));
        }
    }
}
