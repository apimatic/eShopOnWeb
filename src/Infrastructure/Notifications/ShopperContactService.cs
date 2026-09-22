using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.Infrastructure.Notifications;

public sealed class ShopperContactService : IShopperContactService
{
    private readonly IRepository<ContactNumber> _contacts;
    private readonly ISmsGateway _gateway;

    public ShopperContactService(IRepository<ContactNumber> contacts, ISmsGateway gateway)
    {
        _contacts = contacts;
        _gateway = gateway;
    }

    public async Task<ContactRegistrationResult> RegisterAsync(string buyerId, string phoneNumber,
        CancellationToken ct)
    {
        // Reject an unusable destination here (at registration), not when a later message fails to send.
        var lookup = await _gateway.LookupNumberAsync(phoneNumber, ct);
        if (!lookup.IsValid || string.IsNullOrEmpty(lookup.CanonicalNumber))
        {
            return new ContactRegistrationResult(false, 0, null);
        }

        // Store the provider's canonical form, not whatever the caller typed.
        var contact = new ContactNumber(buyerId, lookup.CanonicalNumber!);
        await _contacts.AddAsync(contact, ct);
        return new ContactRegistrationResult(true, contact.Id, contact.PhoneNumber);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken ct) =>
        await _contacts.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), ct);

    public async Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken ct)
    {
        // Scoped to the owner so one shopper can never delete another's number.
        var contact = await _contacts.FirstOrDefaultAsync(
            new ContactNumberByIdForBuyerSpecification(contactNumberId, buyerId), ct);
        if (contact == null)
        {
            return false;
        }

        await _contacts.DeleteAsync(contact, ct);
        return true;
    }
}
