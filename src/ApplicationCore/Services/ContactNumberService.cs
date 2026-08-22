using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IPhoneNumberLookupClient _lookupClient;
    private readonly IRepository<ShopperContactNumber> _repository;
    private readonly IOrderNotificationService _notificationService;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IPhoneNumberLookupClient lookupClient,
        IRepository<ShopperContactNumber> repository,
        IOrderNotificationService notificationService,
        IAppLogger<ContactNumberService> logger)
    {
        _lookupClient = lookupClient;
        _repository = repository;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task<ShopperContactNumber> RegisterAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var trimmed = phoneNumber?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new InvalidContactNumberException("A phone number is required.");
        }

        PhoneNumberLookupResult lookup;
        try
        {
            lookup = await _lookupClient.LookupAsync(trimmed, cancellationToken);
        }
        catch (PhoneNumberLookupException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new PhoneNumberLookupException("The provider could not look up the phone number.", ex);
        }

        if (!lookup.IsValid || string.IsNullOrWhiteSpace(lookup.CanonicalNumber))
        {
            throw new InvalidContactNumberException("The provider does not consider this a usable destination.");
        }

        var canonical = lookup.CanonicalNumber;

        var existingActive = await _repository.FirstOrDefaultAsync(
            new ActiveContactNumberByCanonicalSpecification(canonical), cancellationToken);
        if (existingActive is not null)
        {
            if (!string.Equals(existingActive.BuyerId, buyerId, StringComparison.Ordinal))
            {
                throw new DuplicateException("This number is already registered.");
            }

            return existingActive;
        }

        var owned = await _repository.ListAsync(new ShopperContactNumbersSpecification(buyerId, activeOnly: false), cancellationToken);
        var previouslyOwned = owned.FirstOrDefault(c =>
            string.Equals(c.CanonicalNumber, canonical, StringComparison.Ordinal));
        if (previouslyOwned is not null)
        {
            previouslyOwned.Reactivate();
            await _repository.UpdateAsync(previouslyOwned, cancellationToken);
            return previouslyOwned;
        }

        var created = new ShopperContactNumber(buyerId, canonical);
        return await _repository.AddAsync(created, cancellationToken);
    }

    public async Task<IReadOnlyList<ShopperContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _repository.ListAsync(new ShopperContactNumbersSpecification(buyerId), cancellationToken);
    }

    public async Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var contact = await _repository.FirstOrDefaultAsync(
            new ShopperContactNumberByIdSpecification(buyerId, contactNumberId), cancellationToken);
        if (contact is null || !contact.IsActive)
        {
            throw new ContactNumberNotFoundException(contactNumberId);
        }

        contact.Deactivate();
        await _repository.UpdateAsync(contact, cancellationToken);

        try
        {
            await _notificationService.CancelScheduledForContactAsync(contact.Id, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Contact number {ContactNumberId} was removed but scheduled follow-ups could not all be cancelled: {Message}", contact.Id, ex.Message);
        }
    }
}
