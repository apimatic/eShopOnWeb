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
    private readonly IRepository<ContactNumber> _repository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IMessagingProvider _messagingProvider;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ContactNumber> repository,
        IRepository<OrderNotification> notificationRepository,
        IMessagingProvider messagingProvider,
        IAppLogger<ContactNumberService> logger)
    {
        _repository = repository;
        _notificationRepository = notificationRepository;
        _messagingProvider = messagingProvider;
        _logger = logger;
    }

    public async Task<ContactNumber> RegisterAsync(string buyerId, string phoneNumber, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw new InvalidContactNumberException("A mobile number is required.");
        }

        PhoneLookupResult lookup;
        try
        {
            lookup = await _messagingProvider.LookupAsync(phoneNumber.Trim(), ct);
        }
        catch (MessagingProviderException ex) when (ex.HttpStatus is >= 400 and < 500 and not 401 and not 403 and not 429)
        {
            throw new InvalidContactNumberException();
        }

        if (!lookup.IsUsable || string.IsNullOrEmpty(lookup.CanonicalNumber))
        {
            throw new InvalidContactNumberException();
        }

        var existing = await _repository.FirstOrDefaultAsync(
            new ContactNumberByCanonicalSpecification(buyerId, lookup.CanonicalNumber), ct);
        if (existing != null)
        {
            return existing;
        }

        var contact = new ContactNumber(buyerId, lookup.CanonicalNumber, lookup.LineType);
        return await _repository.AddAsync(contact, ct);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListForBuyerAsync(string buyerId, CancellationToken ct)
    {
        return await _repository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), ct);
    }

    public async Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken ct)
    {
        var contact = await _repository.GetByIdAsync(contactNumberId, ct);
        if (contact == null || contact.BuyerId != buyerId)
        {
            throw new EntityNotFoundException("Contact number");
        }

        var canonical = contact.CanonicalNumber;
        await _repository.DeleteAsync(contact, ct);

        var pending = await _notificationRepository.ListAsync(
            new PendingFollowUpsByNumberSpecification(canonical), ct);
        foreach (var notification in pending)
        {
            if (string.IsNullOrEmpty(notification.ProviderSid))
            {
                continue;
            }

            try
            {
                var updated = await _messagingProvider.CancelScheduledAsync(notification.ProviderSid, ct);
                notification.RecordProviderResult(
                    updated.Sid,
                    updated.Status,
                    updated.ErrorCode,
                    updated.ErrorMessage,
                    updated.Body);
                await _notificationRepository.UpdateAsync(notification, ct);
            }
            catch (Exception)
            {
                _logger.LogWarning(
                    "Could not cancel scheduled notification {NotificationId} after contact number {ContactNumberId} was removed.",
                    notification.Id,
                    contactNumberId);
            }
        }

        _logger.LogInformation("Removed contact number {ContactNumberId} for a shopper.", contactNumberId);
    }

    public async Task<ContactNumber?> GetPreferredAsync(string buyerId, CancellationToken ct)
    {
        var numbers = await ListForBuyerAsync(buyerId, ct);
        return numbers.FirstOrDefault();
    }
}
