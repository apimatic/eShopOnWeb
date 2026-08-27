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
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly ISmsService _smsService;

    public ContactNumberService(IRepository<ContactNumber> contactNumberRepository, ISmsService smsService)
    {
        _contactNumberRepository = contactNumberRepository;
        _smsService = smsService;
    }

    public async Task<ContactNumber> RegisterAsync(string ownerId, string phoneNumber, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(phoneNumber, nameof(phoneNumber));

        var validation = await _smsService.ValidatePhoneNumberAsync(phoneNumber, ct);
        if (!validation.IsValid || string.IsNullOrEmpty(validation.CanonicalNumber))
        {
            throw new BadRequestException(
                $"The phone number is not a usable destination ({validation.FailureReason ?? "invalid number"}).");
        }

        var existing = await _contactNumberRepository.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), ct);
        if (existing.Any(c => c.PhoneNumber == validation.CanonicalNumber))
        {
            throw new DuplicateException("This phone number is already registered.");
        }

        var contactNumber = new ContactNumber(ownerId, validation.CanonicalNumber);
        return await _contactNumberRepository.AddAsync(contactNumber, ct);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string ownerId, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        return await _contactNumberRepository.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), ct);
    }

    public async Task DeleteAsync(string ownerId, int contactNumberId, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));

        var contactNumber = await _contactNumberRepository.GetByIdAsync(contactNumberId, ct);
        if (contactNumber is null || contactNumber.OwnerId != ownerId)
        {
            throw new EntityNotFoundException($"Contact number {contactNumberId} was not found.");
        }

        await _contactNumberRepository.DeleteAsync(contactNumber, ct);
    }

    public async Task<ContactNumber?> GetPrimaryAsync(string ownerId, CancellationToken ct = default)
    {
        var numbers = await ListAsync(ownerId, ct);
        return numbers.FirstOrDefault();
    }
}
