using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
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

    public async Task<ContactNumber> RegisterAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(phoneNumber, nameof(phoneNumber));

        SmsLookupResult lookup;
        try
        {
            lookup = await _smsGateway.LookupDestinationAsync(phoneNumber, cancellationToken);
        }
        catch (SmsProviderException ex) when (ex.Kind == SmsProviderFailureKind.CallerRejected)
        {
            throw new UnusableContactNumberException("The provider does not consider this a usable destination.");
        }

        if (!lookup.IsUsable || string.IsNullOrWhiteSpace(lookup.CanonicalNumber))
        {
            var detail = lookup.ValidationErrors.Count > 0
                ? $" The provider reported: {string.Join(", ", lookup.ValidationErrors)}."
                : string.Empty;
            throw new UnusableContactNumberException(
                "The provider does not consider this a usable destination." + detail,
                lookup.ValidationErrors);
        }

        var canonical = lookup.CanonicalNumber;
        var existing = await _repository.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndCanonicalSpecification(buyerId, canonical),
            cancellationToken);
        if (existing is not null)
        {
            throw new DuplicateException("This contact number is already registered.");
        }

        var contact = new ContactNumber(buyerId, canonical);
        await _repository.AddAsync(contact, cancellationToken);
        _logger.LogInformation("Registered a contact number for buyer {BuyerId} with id {ContactNumberId}.", buyerId, contact.Id);
        return contact;
    }

    public async Task<IReadOnlyList<ContactNumber>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _repository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var contact = await _repository.GetByIdAsync(contactNumberId, cancellationToken);
        if (contact is null || contact.BuyerId != buyerId)
        {
            throw new ContactNumberNotFoundException(contactNumberId);
        }

        await _repository.DeleteAsync(contact, cancellationToken);
        _logger.LogInformation("Removed contact number {ContactNumberId} for buyer {BuyerId}.", contactNumberId, buyerId);
    }
}
