using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly ITwilioLookupClient _lookupClient;
    private readonly IRepository<ShopperContactNumber> _repository;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        ITwilioLookupClient lookupClient,
        IRepository<ShopperContactNumber> repository,
        IAppLogger<ContactNumberService> logger)
    {
        _lookupClient = lookupClient;
        _repository = repository;
        _logger = logger;
    }

    public async Task<ShopperContactNumber> RegisterAsync(
        string buyerId,
        string phoneNumber,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw new BadRequestException("A phone number is required.");
        }

        var lookup = await _lookupClient.LookupAsync(phoneNumber.Trim(), cancellationToken);
        if (!lookup.Valid || string.IsNullOrWhiteSpace(lookup.CanonicalPhoneNumber))
        {
            var reason = lookup.ValidationError ?? "not a usable destination";
            _logger.LogWarning("Rejected contact number registration: {Reason}", reason);
            throw new BadRequestException("The provider does not consider this number a usable destination.");
        }

        var canonical = lookup.CanonicalPhoneNumber;
        var existing = await _repository.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndPhoneSpecification(buyerId, canonical),
            cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var created = new ShopperContactNumber(buyerId, canonical);
        await _repository.AddAsync(created, cancellationToken);
        _logger.LogInformation("Registered a contact number for buyer {BuyerId}", buyerId);
        return created;
    }

    public async Task<IReadOnlyList<ShopperContactNumber>> ListAsync(
        string buyerId,
        CancellationToken cancellationToken = default)
    {
        var items = await _repository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return items;
    }

    public Task<IReadOnlyList<ShopperContactNumber>> ListActiveAsync(
        string buyerId,
        CancellationToken cancellationToken = default)
        => ListAsync(buyerId, cancellationToken);

    public async Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        var existing = await _repository.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndIdSpecification(buyerId, contactNumberId),
            cancellationToken);
        if (existing is null)
        {
            throw new NotFoundException("Contact number not found.");
        }

        await _repository.DeleteAsync(existing, cancellationToken);
        _logger.LogInformation("Removed contact number {ContactNumberId} for buyer {BuyerId}", contactNumberId, buyerId);
    }
}
