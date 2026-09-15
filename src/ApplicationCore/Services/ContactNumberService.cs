using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly ITwilioMessagingGateway _messagingGateway;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ContactNumber> contactNumberRepository,
        ITwilioMessagingGateway messagingGateway,
        IAppLogger<ContactNumberService> logger)
    {
        _contactNumberRepository = contactNumberRepository;
        _messagingGateway = messagingGateway;
        _logger = logger;
    }

    public async Task<ContactNumberRegistrationResult> RegisterAsync(
        string buyerId, string rawPhoneNumber, CancellationToken cancellationToken)
    {
        // Reject a number the provider does not consider a usable destination here, at registration time,
        // rather than when a message later fails to go out. A provider/transport failure propagates.
        var validation = await _messagingGateway.ValidatePhoneNumberAsync(rawPhoneNumber, cancellationToken);

        if (!validation.IsValid || string.IsNullOrEmpty(validation.CanonicalNumber))
        {
            _logger.LogWarning("Rejected a contact-number registration for buyer {BuyerId}: not a usable destination.", buyerId);
            return new ContactNumberRegistrationResult(false, 0, null,
                validation.Reason ?? "The number is not a valid SMS destination.");
        }

        var canonical = validation.CanonicalNumber;

        // Store the provider's canonical form. If this exact number is already on file for the shopper,
        // keep it idempotent and return the existing registration.
        var existing = await _contactNumberRepository.ListAsync(
            new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        foreach (var current in existing)
        {
            if (current.PhoneNumber == canonical)
            {
                return new ContactNumberRegistrationResult(true, current.Id, canonical, null);
            }
        }

        var contactNumber = new ContactNumber(buyerId, canonical);
        await _contactNumberRepository.AddAsync(contactNumber, cancellationToken);

        _logger.LogInformation("Registered a contact number (id {ContactNumberId}) for buyer {BuyerId}.",
            contactNumber.Id, buyerId);

        return new ContactNumberRegistrationResult(true, contactNumber.Id, canonical, null);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken)
    {
        return await _contactNumberRepository.ListAsync(
            new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken)
    {
        // Scope strictly to the caller's own number — one shopper can never delete another's.
        var contactNumber = await _contactNumberRepository.FirstOrDefaultAsync(
            new ContactNumberByIdForBuyerSpecification(contactNumberId, buyerId), cancellationToken);

        if (contactNumber is null)
        {
            return false;
        }

        await _contactNumberRepository.DeleteAsync(contactNumber, cancellationToken);
        _logger.LogInformation("Deleted contact number (id {ContactNumberId}) for buyer {BuyerId}.",
            contactNumberId, buyerId);
        return true;
    }
}
