using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly ISmsProvider _smsProvider;

    public ContactNumberService(IRepository<ContactNumber> contactNumberRepository, ISmsProvider smsProvider)
    {
        _contactNumberRepository = contactNumberRepository;
        _smsProvider = smsProvider;
    }

    public async Task<ContactNumber> RegisterAsync(string ownerId, string phoneNumber, CancellationToken cancellationToken = default)
    {
        var validation = await _smsProvider.ValidatePhoneNumberAsync(phoneNumber, cancellationToken);
        if (!validation.IsValid || string.IsNullOrEmpty(validation.CanonicalNumber))
        {
            throw new InvalidPhoneNumberException(
                $"The phone number is not a usable destination: {validation.Error ?? "invalid number"}");
        }

        var contactNumber = new ContactNumber(ownerId, validation.CanonicalNumber);
        return await _contactNumberRepository.AddAsync(contactNumber, cancellationToken);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        return await _contactNumberRepository.ListAsync(new ContactNumbersByOwnerSpec(ownerId), cancellationToken);
    }

    public async Task<bool> DeleteAsync(string ownerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        var contactNumber = await _contactNumberRepository.FirstOrDefaultAsync(
            new ContactNumberByIdAndOwnerSpec(contactNumberId, ownerId), cancellationToken);
        if (contactNumber is null)
        {
            return false;
        }

        await _contactNumberRepository.DeleteAsync(contactNumber, cancellationToken);
        return true;
    }
}
