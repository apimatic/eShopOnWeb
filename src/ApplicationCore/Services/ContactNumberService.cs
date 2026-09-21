using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _repository;
    private readonly ISmsGateway _smsGateway;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ContactNumber> repository,
        ISmsGateway smsGateway,
        IAppLogger<ContactNumberService> logger)
    {
        _repository = repository;
        _smsGateway = smsGateway;
        _logger = logger;
    }

    public async Task<RegisterContactNumberResult> RegisterAsync(string buyerId, string rawNumber, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        if (string.IsNullOrWhiteSpace(rawNumber))
        {
            return new RegisterContactNumberResult(false, null, "A mobile number is required.");
        }

        // Reject an unusable destination here (at registration), not when a message later fails to go
        // out. The provider's canonical E.164 form — not the caller's raw text — is what we store.
        var validation = await _smsGateway.ValidateNumberAsync(rawNumber, ct);
        if (!validation.IsUsable || string.IsNullOrEmpty(validation.CanonicalE164))
        {
            _logger.LogWarning("Rejected contact-number registration for buyer {BuyerId}: not a usable destination.", buyerId);
            return new RegisterContactNumberResult(false, null, validation.Reason ?? "The number is not a usable destination.");
        }

        var contactNumber = new ContactNumber(buyerId, validation.CanonicalE164!);
        await _repository.AddAsync(contactNumber, ct);

        _logger.LogInformation("Registered contact number {ContactNumberId} for buyer {BuyerId}.", contactNumber.Id, buyerId);
        return new RegisterContactNumberResult(true, contactNumber.Id, null);
    }

    public async Task<IReadOnlyList<ContactNumberView>> ListAsync(string buyerId, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var numbers = await _repository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), ct);
        return numbers.Select(n => new ContactNumberView(n.Id, n.E164Number)).ToList();
    }

    public async Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        // Scoped by buyer so one shopper can never delete another's number.
        var contactNumber = await _repository.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndIdSpecification(buyerId, contactNumberId), ct);
        if (contactNumber is null)
        {
            return false;
        }

        await _repository.DeleteAsync(contactNumber, ct);
        _logger.LogInformation("Removed contact number {ContactNumberId} for buyer {BuyerId}.", contactNumberId, buyerId);
        return true;
    }
}
