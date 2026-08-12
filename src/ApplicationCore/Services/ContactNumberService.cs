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
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly ISmsService _smsService;

    public ContactNumberService(IRepository<ContactNumber> contactNumberRepository, ISmsService smsService)
    {
        _contactNumberRepository = contactNumberRepository;
        _smsService = smsService;
    }

    public async Task<ContactNumber> RegisterAsync(string buyerId, string rawPhoneNumber, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrWhiteSpace(rawPhoneNumber, nameof(rawPhoneNumber));

        // Reject a number the provider does not consider a usable destination here, at registration,
        // rather than at the moment a message would fail. Store the provider's canonical form.
        var validation = await _smsService.ValidatePhoneNumberAsync(rawPhoneNumber, cancellationToken);
        if (!validation.IsValid || string.IsNullOrEmpty(validation.CanonicalNumber))
        {
            throw new InvalidPhoneNumberException("The number provided is not a valid, reachable mobile destination.");
        }

        var canonical = validation.CanonicalNumber;

        // Don't duplicate a number the shopper already has on file.
        var existing = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        var already = existing.FirstOrDefault(c => c.PhoneNumber == canonical);
        if (already is not null)
        {
            return already;
        }

        var contactNumber = new ContactNumber(buyerId, canonical);
        return await _contactNumberRepository.AddAsync(contactNumber, cancellationToken);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task<bool> RemoveAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var contactNumber = await _contactNumberRepository.GetByIdAsync(contactNumberId, cancellationToken);

        // Not found, or owned by someone else — indistinguishable to the caller by design.
        if (contactNumber is null || contactNumber.BuyerId != buyerId)
        {
            return false;
        }

        await _contactNumberRepository.DeleteAsync(contactNumber, cancellationToken);
        return true;
    }
}
