using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.ApplicationCore.Sms;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly ISmsGateway _smsGateway;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(IRepository<ContactNumber> contactNumbers, ISmsGateway smsGateway,
        IAppLogger<ContactNumberService> logger)
    {
        _contactNumbers = contactNumbers;
        _smsGateway = smsGateway;
        _logger = logger;
    }

    public async Task<RegisterContactNumberResult> RegisterAsync(string buyerId, string rawNumber, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (string.IsNullOrWhiteSpace(rawNumber))
            return RegisterContactNumberResult.Rejected("A phone number is required.");

        // Reject an unusable destination here, up front, rather than when a message later fails to go out.
        var lookup = await _smsGateway.LookupAsync(rawNumber, cancellationToken);
        if (!lookup.IsValid || string.IsNullOrEmpty(lookup.CanonicalE164))
        {
            _logger.LogWarning("Rejected contact number registration for buyer {BuyerId}: not a usable destination.", buyerId);
            return RegisterContactNumberResult.Rejected(lookup.Reason ?? "The number is not a usable destination.");
        }

        // Store the provider's canonical form, and avoid duplicating a number the shopper already has.
        var existing = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        foreach (var current in existing)
        {
            if (current.PhoneNumber == lookup.CanonicalE164)
                return RegisterContactNumberResult.Ok(current.Id, current.PhoneNumber);
        }

        var contactNumber = new ContactNumber(buyerId, lookup.CanonicalE164);
        await _contactNumbers.AddAsync(contactNumber, cancellationToken);
        _logger.LogInformation("Registered a contact number (id {ContactNumberId}) for buyer {BuyerId}.", contactNumber.Id, buyerId);
        return RegisterContactNumberResult.Ok(contactNumber.Id, contactNumber.PhoneNumber);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task<bool> RemoveAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        // Scoped by owner: a shopper can only remove their own number.
        var contactNumber = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByIdSpecification(contactNumberId, buyerId), cancellationToken);
        if (contactNumber is null)
            return false;

        await _contactNumbers.DeleteAsync(contactNumber, cancellationToken);
        _logger.LogInformation("Removed contact number (id {ContactNumberId}) for buyer {BuyerId}.", contactNumberId, buyerId);
        return true;
    }
}
