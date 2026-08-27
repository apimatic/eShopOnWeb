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
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly ISmsClient _smsClient;

    public ContactNumberService(IRepository<ContactNumber> contactNumberRepository, ISmsClient smsClient)
    {
        _contactNumberRepository = contactNumberRepository;
        _smsClient = smsClient;
    }

    public async Task<ContactNumber> RegisterAsync(string buyerId, string rawNumber, string? countryCode, CancellationToken cancellationToken = default)
    {
        var validation = await _smsClient.ValidatePhoneNumberAsync(rawNumber, countryCode, cancellationToken);
        if (!validation.IsValid || string.IsNullOrEmpty(validation.CanonicalNumber))
        {
            throw new InvalidPhoneNumberException(validation.ValidationErrors);
        }

        var existingSpec = new ContactNumbersByBuyerSpecification(buyerId);
        var existing = await _contactNumberRepository.ListAsync(existingSpec, cancellationToken);
        var duplicate = existing.FirstOrDefault(c => c.PhoneNumber == validation.CanonicalNumber);
        if (duplicate != null)
        {
            return duplicate;
        }

        var contactNumber = new ContactNumber(buyerId, validation.CanonicalNumber, validation.NationalFormat);
        return await _contactNumberRepository.AddAsync(contactNumber, cancellationToken);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var spec = new ContactNumbersByBuyerSpecification(buyerId);
        return await _contactNumberRepository.ListAsync(spec, cancellationToken);
    }

    public async Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        var contactNumber = await _contactNumberRepository.GetByIdAsync(contactNumberId, cancellationToken);
        if (contactNumber == null || contactNumber.BuyerId != buyerId)
        {
            return false;
        }

        await _contactNumberRepository.DeleteAsync(contactNumber, cancellationToken);
        return true;
    }
}
