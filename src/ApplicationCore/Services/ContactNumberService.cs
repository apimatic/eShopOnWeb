using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Extensions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _repository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly ITwilioLookupClient _lookupClient;
    private readonly ITwilioMessagingClient _messagingClient;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ContactNumber> repository,
        IRepository<OrderNotification> notificationRepository,
        ITwilioLookupClient lookupClient,
        ITwilioMessagingClient messagingClient,
        IAppLogger<ContactNumberService> logger)
    {
        _repository = repository;
        _notificationRepository = notificationRepository;
        _lookupClient = lookupClient;
        _messagingClient = messagingClient;
        _logger = logger;
    }

    public async Task<ContactNumber> RegisterAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw new InvalidContactNumberException("A phone number is required.");
        }

        var lookup = await _lookupClient.LookupAsync(phoneNumber, cancellationToken);
        if (!lookup.Valid || string.IsNullOrWhiteSpace(lookup.CanonicalNumber))
        {
            var errors = lookup.ValidationErrors is { Length: > 0 }
                ? string.Join(", ", lookup.ValidationErrors.Where(e => !string.IsNullOrWhiteSpace(e)))
                : "the provider does not consider this a usable destination";
            throw new InvalidContactNumberException($"The phone number is not a usable destination: {errors}.");
        }

        var existing = await _repository.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndCanonicalSpecification(buyerId, lookup.CanonicalNumber),
            cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        var contact = new ContactNumber(buyerId, lookup.CanonicalNumber);
        return await _repository.AddAsync(contact, cancellationToken);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _repository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        var contact = await _repository.FirstOrDefaultAsync(
            new ContactNumberByIdAndBuyerSpecification(contactNumberId, buyerId),
            cancellationToken);
        if (contact == null)
        {
            throw new KeyNotFoundException("Contact number was not found.");
        }

        await CancelPendingMessagesForNumberAsync(buyerId, contact.CanonicalNumber, cancellationToken);
        await _repository.DeleteAsync(contact, cancellationToken);
    }

    public async Task<ContactNumber?> GetPreferredForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var numbers = await _repository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return numbers.FirstOrDefault();
    }

    public async Task<bool> IsActiveForBuyerAsync(string buyerId, string canonicalNumber, CancellationToken cancellationToken = default)
    {
        var existing = await _repository.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndCanonicalSpecification(buyerId, canonicalNumber),
            cancellationToken);
        return existing != null;
    }

    private async Task CancelPendingMessagesForNumberAsync(string buyerId, string canonicalNumber, CancellationToken cancellationToken)
    {
        var notifications = await _notificationRepository.ListAsync(
            new OrderNotificationsByBuyerSpecification(buyerId),
            cancellationToken);

        foreach (var notification in notifications.Where(n =>
                     n.DestinationNumber == canonicalNumber &&
                     !string.IsNullOrWhiteSpace(n.ProviderMessageSid) &&
                     string.Equals(n.ProviderStatus, "scheduled", System.StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var updated = await _messagingClient.CancelScheduledAsync(notification.ProviderMessageSid!, cancellationToken);
                notification.ApplyProviderResult(updated.Sid, updated.Status, updated.ErrorCode, updated.ErrorMessage, updated.Body);
                await _notificationRepository.UpdateAsync(notification, cancellationToken);
            }
            catch (System.Exception ex)
            {
                _logger.LogWarning("Failed to cancel scheduled notification {NotificationId} after contact removal: {Message}", notification.Id, LogSanitizer.RedactPhoneNumbers(ex.Message));
            }
        }
    }
}
