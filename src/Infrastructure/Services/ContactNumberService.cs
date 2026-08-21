using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly ITwilioLookupClient _lookupClient;
    private readonly IRepository<ContactNumber> _repository;

    public ContactNumberService(ITwilioLookupClient lookupClient, IRepository<ContactNumber> repository)
    {
        _lookupClient = lookupClient;
        _repository = repository;
    }

    public async Task<ContactNumber> RegisterAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw new InvalidContactNumberException("A phone number is required.");
        }

        var lookup = await _lookupClient.LookupAsync(phoneNumber, cancellationToken);
        if (!lookup.Valid || string.IsNullOrWhiteSpace(lookup.CanonicalNumber))
        {
            var reason = lookup.ValidationErrors.Count > 0
                ? string.Join(", ", lookup.ValidationErrors)
                : "the provider does not consider it a usable destination";
            throw new InvalidContactNumberException($"The phone number is not a usable destination: {reason}.");
        }

        var existing = await _repository.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndCanonicalSpecification(buyerId, lookup.CanonicalNumber),
            cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var contact = new ContactNumber(buyerId, lookup.CanonicalNumber);
        return await _repository.AddAsync(contact, cancellationToken);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var numbers = await _repository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return numbers;
    }

    public async Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        var contact = await _repository.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndIdSpecification(buyerId, contactNumberId),
            cancellationToken);
        if (contact is null)
        {
            throw new KeyNotFoundException("Contact number not found.");
        }

        await _repository.DeleteAsync(contact, cancellationToken);
    }

    public async Task<string?> GetPreferredCanonicalNumberAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var numbers = await ListAsync(buyerId, cancellationToken);
        return numbers.FirstOrDefault()?.CanonicalNumber;
    }
}
