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
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly ISmsNotificationGateway _gateway;

    public ContactNumberService(IRepository<ContactNumber> contactNumbers, ISmsNotificationGateway gateway)
    {
        _contactNumbers = contactNumbers;
        _gateway = gateway;
    }

    public async Task<RegisterContactNumberResult> RegisterAsync(string buyerId, string rawNumber, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (string.IsNullOrWhiteSpace(rawNumber))
        {
            return RegisterContactNumberResult.Rejected("A phone number is required.");
        }

        // Reject an unusable destination here, at registration — not when a later message fails to go out.
        var validation = await _gateway.ValidateNumberAsync(rawNumber, ct);
        if (!validation.IsUsable || string.IsNullOrEmpty(validation.CanonicalPhoneNumber))
        {
            return RegisterContactNumberResult.Rejected(validation.Reason ?? "The number is not a usable destination.");
        }

        // Store the provider's canonical form. If the shopper already has this exact number, reuse it.
        var existing = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), ct);
        var already = existing.FirstOrDefault(c => c.PhoneNumber == validation.CanonicalPhoneNumber);
        if (already is not null)
        {
            return RegisterContactNumberResult.Registered(already);
        }

        var number = new ContactNumber(buyerId, validation.CanonicalPhoneNumber);
        number = await _contactNumbers.AddAsync(number, ct);
        return RegisterContactNumberResult.Registered(number);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), ct);
    }

    public async Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        // Scoped by owner: another shopper's number is simply not found here, so it can be neither seen nor deleted.
        var number = await _contactNumbers.FirstOrDefaultAsync(new ContactNumberByIdForBuyerSpecification(contactNumberId, buyerId), ct);
        if (number is null)
        {
            return false;
        }
        await _contactNumbers.DeleteAsync(number, ct);
        return true;
    }
}
