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
    private readonly ISmsProvider _smsProvider;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(IRepository<ContactNumber> repository, ISmsProvider smsProvider, IAppLogger<ContactNumberService> logger)
    {
        _repository = repository;
        _smsProvider = smsProvider;
        _logger = logger;
    }

    public async Task<ContactNumber> RegisterAsync(string buyerId, string rawPhoneNumber, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (string.IsNullOrWhiteSpace(rawPhoneNumber))
            throw new BadRequestException("A phone number is required.");

        // Reject a number the provider does not consider a usable destination here, at registration,
        // and keep the provider's canonical form rather than whatever the caller typed.
        var validation = await _smsProvider.ValidateNumberAsync(rawPhoneNumber, cancellationToken);
        if (!validation.IsValid || string.IsNullOrEmpty(validation.CanonicalNumber))
        {
            throw new InvalidPhoneNumberException($"The number is not a usable destination ({validation.Reason ?? "invalid"}).");
        }

        var canonical = validation.CanonicalNumber!;

        // Do not create duplicates for the same shopper.
        var existing = await _repository.FirstOrDefaultAsync(new ContactNumberByNumberForBuyerSpecification(buyerId, canonical), cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var contactNumber = new ContactNumber(buyerId, canonical);
        await _repository.AddAsync(contactNumber, cancellationToken);
        _logger.LogInformation("Registered contact number {Id} for a shopper.", contactNumber.Id);
        return contactNumber;
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _repository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        // Scoped lookup: one shopper can never delete another's number.
        var contactNumber = await _repository.FirstOrDefaultAsync(new ContactNumberByIdForBuyerSpecification(contactNumberId, buyerId), cancellationToken);
        if (contactNumber is null)
        {
            return false;
        }

        await _repository.DeleteAsync(contactNumber, cancellationToken);
        _logger.LogInformation("Removed contact number {Id} for a shopper.", contactNumberId);
        return true;
    }
}
