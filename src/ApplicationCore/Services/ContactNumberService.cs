using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IPhoneNumberValidator _phoneNumberValidator;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ContactNumber> contactNumberRepository,
        IPhoneNumberValidator phoneNumberValidator,
        IAppLogger<ContactNumberService> logger)
    {
        _contactNumberRepository = contactNumberRepository;
        _phoneNumberValidator = phoneNumberValidator;
        _logger = logger;
    }

    public async Task<int> RegisterAsync(string buyerId, string rawNumber, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (string.IsNullOrWhiteSpace(rawNumber))
            throw new InvalidContactNumberException("A phone number is required.");

        // Reject an unusable destination now, at registration, rather than when a message later fails.
        var validation = await _phoneNumberValidator.ValidateAsync(rawNumber, cancellationToken);
        if (!validation.IsValid || string.IsNullOrEmpty(validation.CanonicalNumber))
        {
            var reason = validation.Errors.Count > 0 ? string.Join(", ", validation.Errors) : "not a usable destination";
            // Note: the shopper's number is never written to logs.
            _logger.LogWarning("Rejected a contact number registration for buyer {BuyerId}: {Reason}.", buyerId, reason);
            throw new InvalidContactNumberException($"The number could not be registered ({reason}).");
        }

        // Store the provider's canonical form, not whatever the caller typed.
        var canonical = validation.CanonicalNumber;

        // De-duplicate: if this shopper already has this exact number on file, return it unchanged.
        var existing = await _contactNumberRepository.FirstOrDefaultAsync(
            new ContactNumberByValueForBuyerSpecification(buyerId, canonical), cancellationToken);
        if (existing is not null)
            return existing.Id;

        var contactNumber = new ContactNumber(buyerId, canonical);
        await _contactNumberRepository.AddAsync(contactNumber, cancellationToken);
        _logger.LogInformation("Registered a contact number (id {ContactNumberId}) for buyer {BuyerId}.", contactNumber.Id, buyerId);
        return contactNumber.Id;
    }

    public async Task<IReadOnlyList<ContactNumberView>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var numbers = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return numbers.Select(n => new ContactNumberView(n.Id, n.PhoneNumber, n.RegisteredDate)).ToList();
    }

    public async Task DeleteAsync(int contactNumberId, string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        // Only the owner's number is loadable, so another shopper's number can never be deleted here.
        var contactNumber = await _contactNumberRepository.FirstOrDefaultAsync(
            new ContactNumberByIdForBuyerSpecification(contactNumberId, buyerId), cancellationToken);
        if (contactNumber is null)
            throw new NotificationEntityNotFoundException($"Contact number {contactNumberId} was not found.");

        await _contactNumberRepository.DeleteAsync(contactNumber, cancellationToken);
        _logger.LogInformation("Removed contact number {ContactNumberId} for buyer {BuyerId}.", contactNumberId, buyerId);
    }
}
