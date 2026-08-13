using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly ISmsGateway _smsGateway;

    public ContactNumberService(IRepository<ContactNumber> contactNumberRepository, ISmsGateway smsGateway)
    {
        _contactNumberRepository = contactNumberRepository;
        _smsGateway = smsGateway;
    }

    public async Task<ContactNumberRegistrationResult> RegisterAsync(string buyerId, string rawPhoneNumber, CancellationToken cancellationToken = default)
    {
        // Validate with the provider up front (not at the moment a message later fails to go out),
        // and store the provider's own canonical form rather than whatever the caller typed.
        var validation = await _smsGateway.ValidateDestinationAsync(rawPhoneNumber, cancellationToken);
        if (!validation.IsValid || string.IsNullOrWhiteSpace(validation.CanonicalNumber))
            return ContactNumberRegistrationResult.Rejected();

        var contactNumber = new ContactNumber(buyerId, validation.CanonicalNumber!);
        await _contactNumberRepository.AddAsync(contactNumber, cancellationToken);

        return ContactNumberRegistrationResult.Registered(contactNumber.Id, contactNumber.PhoneNumber);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task<bool> DeleteAsync(int contactNumberId, string buyerId, CancellationToken cancellationToken = default)
    {
        // Scoped to the owner: one shopper can never delete another's number.
        var contactNumber = await _contactNumberRepository.FirstOrDefaultAsync(
            new ContactNumberByBuyerSpecification(contactNumberId, buyerId), cancellationToken);
        if (contactNumber is null)
            return false;

        await _contactNumberRepository.DeleteAsync(contactNumber, cancellationToken);
        return true;
    }
}
