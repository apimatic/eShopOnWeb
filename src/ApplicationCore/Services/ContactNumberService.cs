using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly ISmsService _smsService;

    public ContactNumberService(IRepository<ContactNumber> contactNumberRepository, ISmsService smsService)
    {
        _contactNumberRepository = contactNumberRepository;
        _smsService = smsService;
    }

    public async Task<ContactNumber> RegisterAsync(string ownerId, string phoneNumber)
    {
        var validation = await _smsService.ValidatePhoneNumberAsync(phoneNumber);
        if (!validation.IsValid || validation.CanonicalNumber is null)
        {
            var reasons = validation.ValidationErrors.Count > 0
                ? string.Join(", ", validation.ValidationErrors)
                : "the provider does not consider it a usable destination";
            throw new InvalidPhoneNumberException($"The phone number was rejected: {reasons}.");
        }

        var existing = await _contactNumberRepository.ListAsync(new ContactNumbersByOwnerSpecification(ownerId));
        var match = existing.FirstOrDefault(c => c.PhoneNumber == validation.CanonicalNumber);
        if (match != null)
        {
            return match;
        }

        var contactNumber = new ContactNumber(ownerId, validation.CanonicalNumber);
        return await _contactNumberRepository.AddAsync(contactNumber);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string ownerId)
    {
        return await _contactNumberRepository.ListAsync(new ContactNumbersByOwnerSpecification(ownerId));
    }

    public async Task<bool> DeleteAsync(string ownerId, int contactNumberId)
    {
        var contactNumber = await _contactNumberRepository.GetByIdAsync(contactNumberId);
        if (contactNumber == null || contactNumber.OwnerId != ownerId)
        {
            return false;
        }

        await _contactNumberRepository.DeleteAsync(contactNumber);
        return true;
    }
}
