using System.Collections.Generic;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly ISmsSender _smsSender;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(IRepository<ContactNumber> contactNumberRepository, ISmsSender smsSender,
        IAppLogger<ContactNumberService> logger)
    {
        _contactNumberRepository = contactNumberRepository;
        _smsSender = smsSender;
        _logger = logger;
    }

    public async Task<ContactNumberRegistrationResult> RegisterAsync(string buyerId, string rawNumber)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        if (string.IsNullOrWhiteSpace(rawNumber))
        {
            return ContactNumberRegistrationResult.Rejected("A phone number is required.");
        }

        // Validate + canonicalise with the provider before storing anything. An un-sendable number
        // is rejected now, not at the moment a message fails to go out.
        var lookup = await _smsSender.LookupAsync(rawNumber.Trim());
        if (!lookup.Valid || string.IsNullOrEmpty(lookup.CanonicalNumber))
        {
            _logger.LogInformation("Rejected contact number registration for buyer {BuyerId}: not a usable destination.", buyerId);
            return ContactNumberRegistrationResult.Rejected(lookup.ValidationError ?? "The number is not a usable destination.");
        }

        // Store the provider's canonical form, de-duplicating within the shopper's own numbers.
        var existing = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId));
        var already = existing.Find(n => n.PhoneNumber == lookup.CanonicalNumber);
        if (already is not null)
        {
            return ContactNumberRegistrationResult.Success(already);
        }

        var contactNumber = new ContactNumber(buyerId, lookup.CanonicalNumber);
        await _contactNumberRepository.AddAsync(contactNumber);
        _logger.LogInformation("Registered contact number {ContactNumberId} for buyer {BuyerId}.", contactNumber.Id, buyerId);
        return ContactNumberRegistrationResult.Success(contactNumber);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId));
    }

    public async Task<bool> DeleteAsync(string buyerId, int contactNumberId)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        // Scoped by buyer so one shopper can never delete another's number.
        var contactNumber = await _contactNumberRepository.FirstOrDefaultAsync(
            new ContactNumberByIdForBuyerSpecification(contactNumberId, buyerId));
        if (contactNumber is null)
        {
            return false;
        }

        await _contactNumberRepository.DeleteAsync(contactNumber);
        _logger.LogInformation("Removed contact number {ContactNumberId} for buyer {BuyerId}.", contactNumberId, buyerId);
        return true;
    }
}
