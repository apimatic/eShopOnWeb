using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NotFoundException = Microsoft.eShopWeb.ApplicationCore.Exceptions.NotFoundException;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly INotificationGateway _gateway;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        INotificationGateway gateway,
        IAppLogger<ContactNumberService> logger)
    {
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<ContactNumber> RegisterAsync(string ownerId, string rawNumber, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(ownerId, nameof(ownerId));
        Guard.Against.NullOrWhiteSpace(rawNumber, nameof(rawNumber));

        // Reject a number the provider does not consider usable at registration time, not at send time.
        var validation = await _gateway.ValidatePhoneNumberAsync(rawNumber, cancellationToken);
        if (!validation.IsValid || string.IsNullOrWhiteSpace(validation.CanonicalNumber))
        {
            // Never echo the raw number back into the message that ends up in logs/responses.
            throw new PhoneNumberValidationException("The supplied phone number is not a valid, reachable destination.");
        }

        var canonical = validation.CanonicalNumber;

        // Idempotent registration: if this owner already has this canonical number, return it.
        var existing = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByOwnerAndValueSpecification(ownerId, canonical), cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var contactNumber = new ContactNumber(ownerId, canonical);
        await _contactNumbers.AddAsync(contactNumber, cancellationToken);
        _logger.LogInformation("Registered a contact number for owner {0} (id {1}).", ownerId, contactNumber.Id);
        return contactNumber;
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(ownerId, nameof(ownerId));
        return await _contactNumbers.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), cancellationToken);
    }

    public async Task DeleteAsync(string ownerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(ownerId, nameof(ownerId));

        var contactNumber = await _contactNumbers.GetByIdAsync(contactNumberId, cancellationToken);
        // Treat a number owned by someone else exactly like one that does not exist.
        if (contactNumber is null || contactNumber.OwnerId != ownerId)
        {
            throw new NotFoundException($"Contact number {contactNumberId} was not found.");
        }

        // Nothing may be sent to a deleted number again: call off any not-yet-sent messages to it.
        await CancelPendingMessagesToNumberAsync(ownerId, contactNumber.Number, cancellationToken);

        await _contactNumbers.DeleteAsync(contactNumber, cancellationToken);
        _logger.LogInformation("Removed contact number {0} for owner {1}.", contactNumberId, ownerId);
    }

    private async Task CancelPendingMessagesToNumberAsync(string ownerId, string number, CancellationToken cancellationToken)
    {
        var pending = await _notifications.ListAsync(
            new PendingScheduledNotificationsByNumberSpecification(ownerId, number), cancellationToken);

        foreach (var notification in pending)
        {
            try
            {
                if (notification.ProviderMessageSid is not null)
                {
                    await _gateway.CancelScheduledAsync(notification.ProviderMessageSid, cancellationToken);
                }
                notification.MarkCancelled();
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (System.Exception ex)
            {
                // Best effort: removing the number must still succeed even if a cancellation fails.
                _logger.LogWarning("Failed to cancel a pending message (notification {0}) while deleting a number: {1}",
                    notification.Id, ex.Message);
            }
        }
    }
}
