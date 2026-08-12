using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Registers, lists and removes a shopper's contact numbers. Registration validates and
/// canonicalises the number through the provider up front. Numbers are never written to logs.
/// </summary>
public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly ISmsProvider _smsProvider;

    public ContactNumberService(IRepository<ContactNumber> contactNumberRepository, ISmsProvider smsProvider)
    {
        _contactNumberRepository = contactNumberRepository;
        _smsProvider = smsProvider;
    }

    public async Task<RegisterContactNumberResult> RegisterAsync(string ownerId, string rawNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawNumber))
        {
            return RegisterContactNumberResult.Failure(RegisterContactNumberError.Missing);
        }

        var validation = await _smsProvider.ValidateNumberAsync(rawNumber, cancellationToken);
        if (!validation.IsValid || string.IsNullOrEmpty(validation.CanonicalNumber))
        {
            return RegisterContactNumberResult.Failure(RegisterContactNumberError.NotAUsableDestination);
        }

        var canonical = validation.CanonicalNumber;

        // If the shopper already has this canonical number on file, keep it idempotent.
        var existingForOwner = await _contactNumberRepository.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), cancellationToken);
        var duplicate = existingForOwner.FirstOrDefault(c => c.E164Number == canonical);
        if (duplicate is not null)
        {
            return RegisterContactNumberResult.Success(duplicate.Id, canonical);
        }

        var contactNumber = new ContactNumber(ownerId, canonical);
        await _contactNumberRepository.AddAsync(contactNumber, cancellationToken);

        return RegisterContactNumberResult.Success(contactNumber.Id, canonical);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListForOwnerAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        return await _contactNumberRepository.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), cancellationToken);
    }

    public async Task<bool> DeleteAsync(int contactNumberId, string ownerId, CancellationToken cancellationToken = default)
    {
        var contactNumber = await _contactNumberRepository.FirstOrDefaultAsync(
            new ContactNumberByIdForOwnerSpecification(contactNumberId, ownerId), cancellationToken);
        if (contactNumber is null)
        {
            return false;
        }

        await _contactNumberRepository.DeleteAsync(contactNumber, cancellationToken);
        return true;
    }
}
