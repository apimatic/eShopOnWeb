using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

public interface IContactNumberService
{
    /// <summary>
    /// Register a mobile number for a shopper. The provider validates it; a number it does not
    /// consider a usable destination is rejected here (not when a later message fails), and the
    /// provider's own canonical form is what gets stored. Throws <see cref="ApplicationCore.Exceptions.SmsGatewayException"/>
    /// only if the provider itself is unreachable.
    /// </summary>
    Task<ContactNumberRegistration> RegisterAsync(string buyerId, string rawNumber, CancellationToken ct);

    /// <summary>The caller's own registered numbers.</summary>
    Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken ct);

    /// <summary>Remove one of the caller's numbers. Returns false if it does not exist or is not the caller's.</summary>
    Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken ct);
}

public sealed class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _repository;
    private readonly ISmsGateway _gateway;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ContactNumber> repository,
        ISmsGateway gateway,
        IAppLogger<ContactNumberService> logger)
    {
        _repository = repository;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<ContactNumberRegistration> RegisterAsync(string buyerId, string rawNumber, CancellationToken ct)
    {
        var validation = await _gateway.ValidateDestinationAsync(rawNumber, ct);
        if (!validation.IsUsableDestination || string.IsNullOrWhiteSpace(validation.CanonicalE164))
        {
            return new ContactNumberRegistration(false, null, null,
                validation.Reason ?? "The number is not a usable SMS destination.");
        }

        var canonical = validation.CanonicalE164;

        // If this shopper already has this exact number on file, don't duplicate it.
        var owned = await _repository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), ct);
        var existing = owned.FirstOrDefault(cn => cn.E164Number == canonical);
        if (existing is not null)
        {
            return new ContactNumberRegistration(true, existing.Id, canonical, null);
        }

        var entity = new ContactNumber(buyerId, canonical);
        await _repository.AddAsync(entity, ct);
        _logger.LogInformation("Contact number registered (id={Id}).", entity.Id); // never log the number itself
        return new ContactNumberRegistration(true, entity.Id, canonical, null);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken ct)
    {
        var owned = await _repository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), ct);
        return owned;
    }

    public async Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken ct)
    {
        var entity = await _repository.GetByIdAsync(contactNumberId, ct);
        // Not found, or owned by another shopper — the caller must not be able to tell these apart,
        // and must never act on another shopper's number.
        if (entity is null || entity.BuyerId != buyerId)
        {
            return false;
        }

        await _repository.DeleteAsync(entity, ct);
        _logger.LogInformation("Contact number removed (id={Id}).", contactNumberId);
        return true;
    }
}
