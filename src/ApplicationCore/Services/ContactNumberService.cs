using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _repository;
    private readonly IPhoneNumberLookup _lookup;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ContactNumber> repository,
        IPhoneNumberLookup lookup,
        IAppLogger<ContactNumberService> logger)
    {
        _repository = repository;
        _lookup = lookup;
        _logger = logger;
    }

    public async Task<ContactNumber> RegisterAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw new UnusablePhoneNumberException("A mobile number is required.");
        }

        var lookup = await _lookup.LookupAsync(phoneNumber.Trim(), cancellationToken);
        if (lookup.ProviderUnavailable)
        {
            throw new ProviderUnavailableException("The messaging provider is unavailable.");
        }

        if (!lookup.IsUsable || string.IsNullOrEmpty(lookup.CanonicalNumber))
        {
            throw new UnusablePhoneNumberException(lookup.RejectionReason ?? "The number is not a usable destination.");
        }

        var existing = await _repository.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndCanonicalSpec(buyerId, lookup.CanonicalNumber), cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var contact = new ContactNumber(buyerId, lookup.CanonicalNumber);
        await _repository.AddAsync(contact, cancellationToken);
        _logger.LogInformation("Registered a contact number for buyer {BuyerId}.", buyerId);
        return contact;
    }

    public async Task<IReadOnlyList<ContactNumber>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken)
    {
        var numbers = await _repository.ListAsync(new ContactNumbersByBuyerSpec(buyerId), cancellationToken);
        return numbers;
    }

    public async Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken)
    {
        var existing = await _repository.FirstOrDefaultAsync(
            new ContactNumberByIdAndBuyerSpec(contactNumberId, buyerId), cancellationToken);
        if (existing is null)
        {
            return false;
        }

        await _repository.DeleteAsync(existing, cancellationToken);
        _logger.LogInformation("Removed contact number {ContactNumberId} for buyer {BuyerId}.", contactNumberId, buyerId);
        return true;
    }

    public async Task<string?> GetPreferredCanonicalNumberAsync(string buyerId, CancellationToken cancellationToken)
    {
        var numbers = await _repository.ListAsync(new ContactNumbersByBuyerSpec(buyerId), cancellationToken);
        return numbers.FirstOrDefault()?.CanonicalNumber;
    }
}
