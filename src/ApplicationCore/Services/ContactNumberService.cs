using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
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

    public async Task<Result<ShopperContactNumber>> RegisterAsync(string buyerId, string rawNumber, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawNumber))
        {
            return AppResult.Invalid<ShopperContactNumber>("A mobile number is required.");
        }

        PhoneLookupResult lookup;
        try
        {
            lookup = await _gateway.LookupNumberAsync(rawNumber, cancellationToken);
        }
        catch (SmsProviderException ex) when (ex.StatusCode is >= 400 and < 500 and not 401 and not 403 and not 429)
        {
            return AppResult.Invalid<ShopperContactNumber>("This number is not a usable destination.");
        }
        catch (SmsProviderException)
        {
            return Result<ShopperContactNumber>.Error("The messaging provider is unavailable.");
        }

        if (!lookup.IsUsableDestination || string.IsNullOrWhiteSpace(lookup.CanonicalNumber))
        {
            return AppResult.Invalid<ShopperContactNumber>(lookup.RejectionReason ?? "This number is not a usable destination.");
        }

        var existing = await _repository.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndCanonicalSpec(buyerId, lookup.CanonicalNumber), cancellationToken);
        if (existing is not null)
        {
            return Result<ShopperContactNumber>.Success(existing);
        }

        var entity = new ShopperContactNumber(buyerId, lookup.CanonicalNumber);
        await _repository.AddAsync(entity, cancellationToken);
        _logger.LogInformation("Registered contact number {ContactNumberId}", entity.Id);
        return Result<ShopperContactNumber>.Success(entity);
    }

    public async Task<IReadOnlyList<ShopperContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken)
    {
        return await _repository.ListAsync(new ContactNumbersByBuyerSpec(buyerId), cancellationToken);
    }

    public async Task<Result> DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken)
    {
        var entity = await _repository.GetByIdAsync(contactNumberId, cancellationToken);
        if (entity is null || entity.BuyerId != buyerId)
        {
            return Result.NotFound();
        }

        await _repository.DeleteAsync(entity, cancellationToken);
        _logger.LogInformation("Deleted contact number {ContactNumberId}", contactNumberId);
        return Result.Success();
    }
}
