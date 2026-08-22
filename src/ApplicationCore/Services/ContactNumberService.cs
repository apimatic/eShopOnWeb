using System;
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
    private static readonly HashSet<string> UnusableLineTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "landline",
        "voicemail",
        "pager",
        "premium",
        "sharedCost"
    };

    private readonly IRepository<ContactNumber> _repository;
    private readonly IPhoneNumberLookupClient _lookupClient;

    public ContactNumberService(IRepository<ContactNumber> repository, IPhoneNumberLookupClient lookupClient)
    {
        _repository = repository;
        _lookupClient = lookupClient;
    }

    public async Task<ContactNumber> RegisterAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(phoneNumber, nameof(phoneNumber));

        var lookup = await _lookupClient.LookupAsync(phoneNumber, cancellationToken);
        if (!lookup.Valid || string.IsNullOrEmpty(lookup.CanonicalPhoneNumber))
        {
            var reasons = lookup.ValidationErrors is { Length: > 0 }
                ? string.Join(", ", lookup.ValidationErrors)
                : "the provider does not consider this a usable destination";
            throw new InvalidContactNumberException($"The phone number is not a usable destination: {reasons}.");
        }

        if (!string.IsNullOrEmpty(lookup.LineType) && UnusableLineTypes.Contains(lookup.LineType))
        {
            throw new InvalidContactNumberException(
                $"The phone number is not a usable SMS destination (line type: {lookup.LineType}).");
        }

        var existingSpec = new ContactNumberByBuyerAndPhoneSpecification(buyerId, lookup.CanonicalPhoneNumber);
        var existing = await _repository.FirstOrDefaultAsync(existingSpec, cancellationToken);
        if (existing is not null)
        {
            throw new DuplicateException("This mobile number is already registered.");
        }

        var contact = new ContactNumber(buyerId, lookup.CanonicalPhoneNumber, lookup.NationalFormat);
        return await _repository.AddAsync(contact, cancellationToken);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var spec = new ActiveContactNumbersByBuyerSpecification(buyerId);
        return await _repository.ListAsync(spec, cancellationToken);
    }

    public async Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var contact = await _repository.GetByIdAsync(contactNumberId, cancellationToken);
        if (contact is null || contact.BuyerId != buyerId || !contact.IsActive)
        {
            throw new ContactNumberNotFoundException(contactNumberId);
        }

        contact.Delete();
        await _repository.UpdateAsync(contact, cancellationToken);
    }

    public async Task<ContactNumber?> GetActiveForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var contacts = await ListAsync(buyerId, cancellationToken);
        return contacts.FirstOrDefault();
    }

    public async Task<bool> IsNumberActiveForBuyerAsync(string buyerId, string canonicalPhoneNumber, CancellationToken cancellationToken = default)
    {
        var spec = new ContactNumberByBuyerAndPhoneSpecification(buyerId, canonicalPhoneNumber);
        var existing = await _repository.FirstOrDefaultAsync(spec, cancellationToken);
        return existing is not null;
    }
}
