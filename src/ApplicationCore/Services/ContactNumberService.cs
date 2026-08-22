using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _repository;
    private readonly ITwilioLookupClient _lookupClient;

    public ContactNumberService(IRepository<ContactNumber> repository, ITwilioLookupClient lookupClient)
    {
        _repository = repository;
        _lookupClient = lookupClient;
    }

    public async Task<ContactNumber> RegisterAsync(string buyerId, string phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw new InvalidContactNumberException("A phone number is required.");
        }

        var lookup = await _lookupClient.LookupAsync(phoneNumber.Trim());
        if (!lookup.Valid || string.IsNullOrWhiteSpace(lookup.CanonicalPhoneNumber))
        {
            var reason = lookup.ValidationErrors.Count > 0
                ? string.Join(", ", lookup.ValidationErrors)
                : "the provider does not consider this a usable destination";
            throw new InvalidContactNumberException(
                $"This number cannot be used as a destination: {reason}.",
                lookup.ValidationErrors);
        }

        var existing = await _repository.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndPhoneSpecification(buyerId, lookup.CanonicalPhoneNumber));
        if (existing is not null)
        {
            if (existing.IsRemoved)
            {
                existing.Restore();
                await _repository.UpdateAsync(existing);
            }

            return existing;
        }

        var contact = new ContactNumber(
            buyerId,
            lookup.CanonicalPhoneNumber,
            lookup.NationalFormat,
            lookup.CountryCode);

        return await _repository.AddAsync(contact);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListForBuyerAsync(string buyerId)
    {
        var numbers = await _repository.ListAsync(new ContactNumbersByBuyerIdSpecification(buyerId));
        return numbers;
    }

    public async Task DeleteAsync(string buyerId, int contactNumberId)
    {
        var contact = await _repository.FirstOrDefaultAsync(
            new ContactNumberByIdAndBuyerIdSpecification(contactNumberId, buyerId));
        if (contact is null)
        {
            throw new KeyNotFoundException("Contact number was not found.");
        }

        contact.Remove();
        await _repository.UpdateAsync(contact);
    }

    public async Task<ContactNumber?> GetActiveDestinationAsync(string buyerId)
    {
        var numbers = await _repository.ListAsync(new ContactNumbersByBuyerIdSpecification(buyerId));
        return numbers.FirstOrDefault();
    }
}
