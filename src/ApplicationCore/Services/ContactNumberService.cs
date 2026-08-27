using System.Collections.Generic;
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
    private readonly IRepository<ContactNumber> _repository;
    private readonly ITwilioLookupClient _lookupClient;

    public ContactNumberService(IRepository<ContactNumber> repository, ITwilioLookupClient lookupClient)
    {
        _repository = repository;
        _lookupClient = lookupClient;
    }

    public async Task<ContactNumber> RegisterAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(phoneNumber, nameof(phoneNumber));

        LookupResult lookup;
        try
        {
            lookup = await _lookupClient.LookupAsync(phoneNumber.Trim(), cancellationToken);
        }
        catch (InvalidContactNumberException)
        {
            throw;
        }
        catch
        {
            throw new InvalidContactNumberException();
        }

        if (!lookup.IsValid || string.IsNullOrWhiteSpace(lookup.CanonicalPhoneNumber))
        {
            throw new InvalidContactNumberException();
        }

        var canonical = lookup.CanonicalPhoneNumber;
        var existing = await _repository.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndCanonicalSpecification(buyerId, canonical),
            cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var contact = new ContactNumber(buyerId, canonical);
        return await _repository.AddAsync(contact, cancellationToken);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _repository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var contact = await _repository.GetByIdAsync(contactNumberId, cancellationToken);
        if (contact is null || !contact.BelongsTo(buyerId))
        {
            return false;
        }

        await _repository.DeleteAsync(contact, cancellationToken);
        return true;
    }

    public async Task<ContactNumber?> GetPreferredAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var numbers = await ListForBuyerAsync(buyerId, cancellationToken);
        return numbers.Count == 0 ? null : numbers[0];
    }
}
