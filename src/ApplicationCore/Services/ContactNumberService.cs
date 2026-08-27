using System.Collections.Generic;
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
    private readonly IPhoneNumberValidator _phoneNumberValidator;

    public ContactNumberService(
        IRepository<ContactNumber> contactNumberRepository,
        IPhoneNumberValidator phoneNumberValidator)
    {
        _contactNumberRepository = contactNumberRepository;
        _phoneNumberValidator = phoneNumberValidator;
    }

    public async Task<ContactNumber> RegisterAsync(string ownerId, string phoneNumber, CancellationToken cancellationToken = default)
    {
        var validation = await _phoneNumberValidator.ValidateAsync(phoneNumber, cancellationToken);
        if (!validation.IsValid || validation.CanonicalNumber is null)
        {
            var reasons = validation.ValidationErrors.Count > 0
                ? string.Join(", ", validation.ValidationErrors)
                : "the provider does not consider it a usable destination";
            throw new InvalidPhoneNumberException($"The phone number was rejected by the provider: {reasons}.");
        }

        var existing = await _contactNumberRepository.CountAsync(
            new ContactNumberByOwnerAndNumberSpecification(ownerId, validation.CanonicalNumber), cancellationToken);
        if (existing > 0)
        {
            throw new DuplicateException("This phone number is already registered.");
        }

        var contactNumber = new ContactNumber(ownerId, validation.CanonicalNumber);
        return await _contactNumberRepository.AddAsync(contactNumber, cancellationToken);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        return await _contactNumberRepository.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), cancellationToken);
    }

    public async Task DeleteAsync(string ownerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        var contactNumber = await _contactNumberRepository.GetByIdAsync(contactNumberId, cancellationToken);
        if (contactNumber is null || contactNumber.OwnerId != ownerId)
        {
            throw new NotFoundException($"Contact number {contactNumberId} was not found.");
        }

        await _contactNumberRepository.DeleteAsync(contactNumber, cancellationToken);
    }
}
