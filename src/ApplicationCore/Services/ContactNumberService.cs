using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IPhoneNumberLookupService _lookup;

    public ContactNumberService(IRepository<ContactNumber> contactNumbers, IPhoneNumberLookupService lookup)
    {
        _contactNumbers = contactNumbers;
        _lookup = lookup;
    }

    public async Task<ContactNumber> RegisterAsync(string buyerId, string rawPhoneNumber, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(rawPhoneNumber, nameof(rawPhoneNumber));

        var lookup = await _lookup.LookupAsync(rawPhoneNumber, cancellationToken);
        if (!lookup.Valid || string.IsNullOrWhiteSpace(lookup.PhoneNumber))
        {
            var reason = lookup.ValidationErrors.Count > 0
                ? string.Join(", ", lookup.ValidationErrors)
                : "the provider does not consider this a usable destination";
            throw new InvalidContactNumberException($"Mobile number was rejected: {reason}.");
        }

        var existing = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndPhoneSpecification(buyerId, lookup.PhoneNumber), cancellationToken);
        if (existing is not null)
        {
            throw new DuplicateException("This mobile number is already registered.");
        }

        var contact = new ContactNumber(buyerId, lookup.PhoneNumber, lookup.NationalFormat);
        return await _contactNumbers.AddAsync(contact, cancellationToken);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var items = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return items;
    }

    public async Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var contact = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndIdSpecification(buyerId, contactNumberId), cancellationToken);
        if (contact is null)
        {
            throw new ContactNumberNotFoundException(contactNumberId);
        }

        await _contactNumbers.DeleteAsync(contact, cancellationToken);
    }
}
