using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>Manages a shopper's mobile numbers, scoped to the owner throughout.</summary>
public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _repository;
    private readonly IPhoneNumberValidator _validator;

    public ContactNumberService(IRepository<ContactNumber> repository, IPhoneNumberValidator validator)
    {
        _repository = repository;
        _validator = validator;
    }

    public async Task<ContactNumberRegistrationResult> RegisterAsync(string buyerId, string rawNumber, CancellationToken cancellationToken = default)
    {
        // Reject an unusable destination here, at registration, not when a later message fails.
        var validation = await _validator.ValidateAsync(rawNumber, cancellationToken);
        if (!validation.IsValid || string.IsNullOrEmpty(validation.CanonicalE164))
        {
            return new ContactNumberRegistrationResult(false, 0, "The number is not a usable messaging destination.");
        }

        var canonical = validation.CanonicalE164!;

        // Store the provider's canonical form. If the shopper already has this exact number, reuse it.
        var existing = await _repository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        var duplicate = existing.FirstOrDefault(c => c.PhoneNumber == canonical);
        if (duplicate is not null)
        {
            return new ContactNumberRegistrationResult(true, duplicate.Id, null);
        }

        var entity = new ContactNumber(buyerId, canonical);
        await _repository.AddAsync(entity, cancellationToken);
        return new ContactNumberRegistrationResult(true, entity.Id, null);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default) =>
        await _repository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);

    public async Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        var entity = await _repository.GetByIdAsync(contactNumberId, cancellationToken);

        // A number belongs to the shopper who registered it: never touch another shopper's.
        if (entity is null || entity.BuyerId != buyerId)
        {
            return false;
        }

        await _repository.DeleteAsync(entity, cancellationToken);
        return true;
    }
}
