using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _repository;
    private readonly ISmsSender _smsSender;

    public ContactNumberService(IRepository<ContactNumber> repository, ISmsSender smsSender)
    {
        _repository = repository;
        _smsSender = smsSender;
    }

    public async Task<ContactNumberDto?> RegisterAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(phoneNumber, nameof(phoneNumber));

        // Reject an unusable destination here (at registration), and store the provider's canonical form.
        var validation = await _smsSender.ValidateAsync(phoneNumber, cancellationToken);
        if (!validation.IsValid || string.IsNullOrEmpty(validation.CanonicalE164))
        {
            return null;
        }

        var contactNumber = new ContactNumber(buyerId, validation.CanonicalE164, validation.CountryCode);
        await _repository.AddAsync(contactNumber, cancellationToken);

        return ToDto(contactNumber);
    }

    public async Task<IReadOnlyList<ContactNumberDto>> ListAsync(string buyerId, CancellationToken cancellationToken)
    {
        var numbers = await _repository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return numbers.Select(ToDto).ToList();
    }

    public async Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken)
    {
        var contactNumber = await _repository.GetByIdAsync(contactNumberId, cancellationToken);

        // A number belongs to the shopper who registered it — never delete another shopper's.
        if (contactNumber is null || contactNumber.BuyerId != buyerId)
        {
            return false;
        }

        await _repository.DeleteAsync(contactNumber, cancellationToken);
        return true;
    }

    private static ContactNumberDto ToDto(ContactNumber c) =>
        new(c.Id, c.PhoneNumber, c.CountryCode, c.RegisteredAtUtc);
}
