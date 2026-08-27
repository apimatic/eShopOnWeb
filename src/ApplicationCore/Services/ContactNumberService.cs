using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly ISmsService _smsService;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(IRepository<ContactNumber> contactNumberRepository,
        ISmsService smsService,
        IAppLogger<ContactNumberService> logger)
    {
        _contactNumberRepository = contactNumberRepository;
        _smsService = smsService;
        _logger = logger;
    }

    public async Task<ContactNumberRegistrationResult> RegisterAsync(string buyerId, string phoneNumber, string? countryCode, CancellationToken cancellationToken = default)
    {
        var validation = await _smsService.ValidatePhoneNumberAsync(phoneNumber, countryCode, cancellationToken);
        if (!validation.IsValid || string.IsNullOrEmpty(validation.CanonicalNumber))
        {
            // Deliberately not logging the rejected number itself.
            _logger.LogInformation("Rejected an unusable contact number for a buyer: {Error}", validation.ValidationError ?? "invalid");
            return ContactNumberRegistrationResult.Failure(validation.ValidationError ?? "The phone number is not a usable destination.");
        }

        var existingSpec = new ContactNumberByBuyerAndNumberSpecification(buyerId, validation.CanonicalNumber);
        var existing = await _contactNumberRepository.FirstOrDefaultAsync(existingSpec, cancellationToken);
        if (existing != null)
        {
            return ContactNumberRegistrationResult.Success(existing);
        }

        var contactNumber = new ContactNumber(buyerId, validation.CanonicalNumber, validation.NationalFormat);
        await _contactNumberRepository.AddAsync(contactNumber, cancellationToken);

        return ContactNumberRegistrationResult.Success(contactNumber);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
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
