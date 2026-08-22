using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ShopperContactNumber> _repository;
    private readonly ISmsNotificationGateway _gateway;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ShopperContactNumber> repository,
        ISmsNotificationGateway gateway,
        IAppLogger<ContactNumberService> logger)
    {
        _repository = repository;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<ShopperContactNumber> RegisterAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw new UnusablePhoneNumberException("A mobile number is required.");
        }

        PhoneLookupResult lookup;
        try
        {
            lookup = await _gateway.LookupDestinationAsync(phoneNumber, cancellationToken);
        }
        catch (SmsProviderException ex) when (IsCallerNumberProblem(ex.StatusCode))
        {
            throw new UnusablePhoneNumberException("This number is not a usable SMS destination.");
        }

        if (!lookup.IsUsable || string.IsNullOrWhiteSpace(lookup.CanonicalNumber))
        {
            throw new UnusablePhoneNumberException(lookup.RejectionReason ?? "This number is not a usable SMS destination.");
        }

        var existing = await _repository.FirstOrDefaultAsync(
            new ContactNumberByCanonicalSpecification(buyerId, lookup.CanonicalNumber),
            cancellationToken);
        if (existing is not null)
        {
            throw new DuplicateException("This mobile number is already registered.");
        }

        var entity = new ShopperContactNumber(buyerId, lookup.CanonicalNumber);
        await _repository.AddAsync(entity, cancellationToken);
        _logger.LogInformation("Registered contact number {ContactNumberId} for a shopper.", entity.Id);
        return entity;
    }

    public async Task<IReadOnlyList<ShopperContactNumber>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken)
    {
        var list = await _repository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return list;
    }

    public async Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken)
    {
        var entity = await _repository.GetByIdAsync(contactNumberId, cancellationToken);
        if (entity is null || entity.BuyerId != buyerId)
        {
            throw new ContactNumberNotFoundException(contactNumberId);
        }

        await _repository.DeleteAsync(entity, cancellationToken);
        _logger.LogInformation("Removed contact number {ContactNumberId}.", contactNumberId);
    }

    public async Task<ShopperContactNumber?> GetPrimaryAsync(string buyerId, CancellationToken cancellationToken)
    {
        var numbers = await ListForBuyerAsync(buyerId, cancellationToken);
        return numbers.FirstOrDefault();
    }

    public async Task<ShopperContactNumber?> GetByIdForBuyerAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken)
    {
        var entity = await _repository.GetByIdAsync(contactNumberId, cancellationToken);
        if (entity is null || entity.BuyerId != buyerId)
        {
            return null;
        }

        return entity;
    }

    private static bool IsCallerNumberProblem(int? statusCode)
    {
        return statusCode is >= 400 and < 500 && statusCode is not 401 and not 403 and not 429;
    }
}
