using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Messaging;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _repository;
    private readonly ITwilioMessagingService _messaging;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ContactNumber> repository,
        ITwilioMessagingService messaging,
        IAppLogger<ContactNumberService> logger)
    {
        _repository = repository;
        _messaging = messaging;
        _logger = logger;
    }

    public async Task<RegisterContactNumberResult> RegisterAsync(string buyerId, string rawNumber, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(rawNumber, nameof(rawNumber));

        // Reject an unusable destination here, at registration — not when a later message fails to go out.
        var validation = await _messaging.ValidateNumberAsync(rawNumber, ct);
        if (!validation.IsValid || string.IsNullOrEmpty(validation.CanonicalNumber))
        {
            _logger.LogWarning("Rejected contact number registration for buyer {BuyerId}: {Reason}",
                buyerId, validation.Reason ?? "not a usable destination");
            return new RegisterContactNumberResult(false, null, null, validation.Reason ?? "The number is not a usable destination.");
        }

        // Store the provider's canonical form, and keep registration idempotent per (buyer, number).
        var existing = await _repository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), ct);
        var already = existing.FirstOrDefault(c => c.PhoneNumber == validation.CanonicalNumber);
        if (already is not null)
        {
            return new RegisterContactNumberResult(true, already.Id, already.PhoneNumber, null);
        }

        var contactNumber = new ContactNumber(buyerId, validation.CanonicalNumber);
        contactNumber = await _repository.AddAsync(contactNumber, ct);

        _logger.LogInformation("Registered contact number {ContactNumberId} for buyer {BuyerId}",
            contactNumber.Id, buyerId);

        return new RegisterContactNumberResult(true, contactNumber.Id, contactNumber.PhoneNumber, null);
    }

    public async Task<IReadOnlyList<ContactNumberView>> ListAsync(string buyerId, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var numbers = await _repository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), ct);
        return numbers
            .Select(c => new ContactNumberView(c.Id, c.PhoneNumber, c.CreatedDate))
            .ToList();
    }

    public async Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var contactNumber = await _repository.GetByIdAsync(contactNumberId, ct);
        // A number belongs to the shopper who registered it: never let another shopper delete it.
        if (contactNumber is null || contactNumber.BuyerId != buyerId)
        {
            return false;
        }

        await _repository.DeleteAsync(contactNumber, ct);
        _logger.LogInformation("Deleted contact number {ContactNumberId} for buyer {BuyerId}",
            contactNumberId, buyerId);
        return true;
    }
}
