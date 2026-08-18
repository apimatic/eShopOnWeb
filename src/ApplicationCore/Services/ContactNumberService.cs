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
    private readonly IRepository<ContactNumber> _repository;
    private readonly IPhoneNumberValidator _validator;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ContactNumber> repository,
        IPhoneNumberValidator validator,
        IAppLogger<ContactNumberService> logger)
    {
        _repository = repository;
        _validator = validator;
        _logger = logger;
    }

    public async Task<ContactNumber> RegisterAsync(string buyerId, string rawNumber, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrWhiteSpace(rawNumber, nameof(rawNumber));

        // Reject an unusable destination here, at registration, rather than at send time.
        var validation = await _validator.ValidateAsync(rawNumber, cancellationToken);
        if (!validation.IsValid || string.IsNullOrEmpty(validation.E164Number))
        {
            throw new InvalidPhoneNumberException(
                "The phone number is not a usable destination.", validation.ValidationErrors);
        }

        var canonical = validation.E164Number;

        // A number never appears in the log; only the outcome does.
        _logger.LogInformation("Contact number registration validated for buyer {0}.", buyerId);

        // Store the provider's canonical form. If the shopper already registered this exact number,
        // keep the existing record rather than creating a duplicate.
        var existing = await _repository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        var duplicate = existing.FirstOrDefault(c => c.E164Number == canonical);
        if (duplicate is not null)
        {
            return duplicate;
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

        var contactNumber = await _repository.FirstOrDefaultAsync(
            new ContactNumberByIdForBuyerSpecification(contactNumberId, buyerId), cancellationToken);

        if (contactNumber is null)
        {
            return false;
        }

        await _repository.DeleteAsync(contactNumber, cancellationToken);
        _logger.LogInformation("Contact number {0} removed for buyer {1}.", contactNumberId, buyerId);
        return true;
    }
}
