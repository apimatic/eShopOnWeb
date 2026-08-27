using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IPhoneNumberValidator _phoneNumberValidator;

    public ContactNumberService(IRepository<ContactNumber> contactNumberRepository,
        IPhoneNumberValidator phoneNumberValidator)
    {
        _contactNumberRepository = contactNumberRepository;
        _phoneNumberValidator = phoneNumberValidator;
    }

    public async Task<ContactNumber> RegisterAsync(string buyerId, string phoneNumber, CancellationToken ct = default)
    {
        var validation = await _phoneNumberValidator.ValidateAsync(phoneNumber, ct);
        if (!validation.IsValid || string.IsNullOrEmpty(validation.CanonicalNumber))
        {
            var reasons = validation.ValidationErrors.Count > 0
                ? string.Join(", ", validation.ValidationErrors)
                : "not a usable destination";
            throw new InvalidPhoneNumberException($"The messaging provider rejected the number: {reasons}.");
        }

        var existing = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), ct);
        foreach (var number in existing)
        {
            if (number.PhoneNumber == validation.CanonicalNumber)
            {
                throw new DuplicateException("This number is already registered.");
            }
        }

        var contactNumber = new ContactNumber(buyerId, validation.CanonicalNumber);
        return await _contactNumberRepository.AddAsync(contactNumber, ct);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken ct = default)
    {
        return await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), ct);
    }

    public async Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken ct = default)
    {
        var contactNumber = await _contactNumberRepository.GetByIdAsync(contactNumberId, ct);
        if (contactNumber == null || contactNumber.BuyerId != buyerId)
        {
            throw new NotFoundException("Contact number not found.");
        }

        await _contactNumberRepository.DeleteAsync(contactNumber, ct);
    }
}
