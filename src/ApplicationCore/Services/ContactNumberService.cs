using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _repository;
    private readonly ISmsNotificationProvider _provider;

    public ContactNumberService(IRepository<ContactNumber> repository, ISmsNotificationProvider provider)
    {
        _repository = repository;
        _provider = provider;
    }

    public async Task<ContactNumber> RegisterAsync(string buyerId, string rawPhoneNumber, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(rawPhoneNumber, nameof(rawPhoneNumber));

        // Reject a number the provider does not consider a usable destination here, at registration,
        // rather than at the moment a message fails to go out.
        var validation = await _provider.ValidatePhoneNumberAsync(rawPhoneNumber, cancellationToken);
        if (!validation.IsValid || string.IsNullOrWhiteSpace(validation.CanonicalNumber))
        {
            throw new InvalidContactNumberException(
                validation.Reason ?? "The number is not a usable destination and cannot be registered.");
        }

        // Store the provider's own canonical form, not whatever the caller typed.
        var canonical = validation.CanonicalNumber!;

        // Registering the same number twice is idempotent — return the existing registration.
        var existing = await _repository.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndNumberSpecification(buyerId, canonical), cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var contactNumber = new ContactNumber(buyerId, canonical);
        return await _repository.AddAsync(contactNumber, cancellationToken);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _repository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task<bool> RemoveAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        // Scope the lookup to the caller so one shopper can never delete another's number.
        var contactNumber = await _repository.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndIdSpecification(buyerId, contactNumberId), cancellationToken);
        if (contactNumber is null)
        {
            return false;
        }

        await _repository.DeleteAsync(contactNumber, cancellationToken);
        return true;
    }
}
