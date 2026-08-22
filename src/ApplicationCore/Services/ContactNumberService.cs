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
    private readonly ISmsNotificationGateway _smsGateway;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ContactNumber> repository,
        ISmsNotificationGateway smsGateway,
        IAppLogger<ContactNumberService> logger)
    {
        _repository = repository;
        _smsGateway = smsGateway;
        _logger = logger;
    }

    public async Task<ContactNumber> RegisterAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw new InvalidContactNumberException("A mobile number is required.");
        }

        var lookup = await _smsGateway.LookupAsync(phoneNumber.Trim(), cancellationToken);
        if (!lookup.IsValid || string.IsNullOrWhiteSpace(lookup.CanonicalNumber))
        {
            var reason = lookup.ValidationErrors.Count > 0
                ? string.Join(", ", lookup.ValidationErrors)
                : "the provider does not consider it a usable destination";
            throw new InvalidContactNumberException($"The mobile number is not a usable destination ({reason}).");
        }

        var existing = await _repository.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndCanonicalSpec(buyerId, lookup.CanonicalNumber),
            cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var contactNumber = new ContactNumber(buyerId, lookup.CanonicalNumber);
        await _repository.AddAsync(contactNumber, cancellationToken);
        _logger.LogInformation("Registered contact number {ContactNumberId} for buyer.", contactNumber.Id);
        return contactNumber;
    }

    public async Task<IReadOnlyList<ContactNumber>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken)
    {
        var numbers = await _repository.ListAsync(new ContactNumbersByBuyerSpec(buyerId), cancellationToken);
        return numbers;
    }

    public async Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken)
    {
        var contactNumber = await _repository.GetByIdAsync(contactNumberId, cancellationToken);
        if (contactNumber is null || contactNumber.BuyerId != buyerId)
        {
            throw new ContactNumberNotFoundException(contactNumberId);
        }

        await _repository.DeleteAsync(contactNumber, cancellationToken);
        _logger.LogInformation("Removed contact number {ContactNumberId} for buyer.", contactNumberId);
    }

    public async Task<ContactNumber?> GetLatestForBuyerAsync(string buyerId, CancellationToken cancellationToken)
    {
        var numbers = await _repository.ListAsync(new ContactNumbersByBuyerSpec(buyerId), cancellationToken);
        return numbers.FirstOrDefault();
    }
}
