using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly ITwilioLookupClient _lookup;
    private readonly OrderSmsDispatcher _smsDispatcher;

    public ContactNumberService(
        IRepository<ContactNumber> contactNumbers,
        ITwilioLookupClient lookup,
        OrderSmsDispatcher smsDispatcher)
    {
        _contactNumbers = contactNumbers;
        _lookup = lookup;
        _smsDispatcher = smsDispatcher;
    }

    public async Task<ContactNumber> RegisterAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw new InvalidContactNumberException("A phone number is required.");
        }

        var lookup = await _lookup.LookupPhoneNumberAsync(phoneNumber.Trim(), cancellationToken);
        if (!lookup.IsValid || string.IsNullOrWhiteSpace(lookup.CanonicalNumber))
        {
            var reason = lookup.ValidationErrors.Count > 0
                ? string.Join(", ", lookup.ValidationErrors)
                : "The provider does not consider this a usable destination.";
            throw new InvalidContactNumberException($"The phone number was rejected: {reason}");
        }

        var existing = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndCanonicalSpecification(buyerId, lookup.CanonicalNumber),
            cancellationToken);

        if (existing is not null)
        {
            if (!existing.IsActive)
            {
                existing.Reactivate();
                await _contactNumbers.UpdateAsync(existing, cancellationToken);
            }

            return existing;
        }

        var contact = new ContactNumber(buyerId, lookup.CanonicalNumber);
        return await _contactNumbers.AddAsync(contact, cancellationToken);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId, activeOnly: true), cancellationToken);
    }

    public async Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        var contact = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByIdForBuyerSpecification(buyerId, contactNumberId),
            cancellationToken);

        if (contact is null || !contact.IsActive)
        {
            return false;
        }

        contact.Deactivate();
        await _contactNumbers.UpdateAsync(contact, cancellationToken);
        await _smsDispatcher.CancelPendingForContactAsync(contact.Id, cancellationToken);
        return true;
    }
}
