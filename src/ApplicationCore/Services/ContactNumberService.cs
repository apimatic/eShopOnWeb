using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
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

    public async Task<ContactNumberRegistrationResult> RegisterAsync(string buyerId, string phoneNumber, CancellationToken ct = default)
    {
        PhoneNumberValidation validation;
        try
        {
            validation = await _smsService.ValidatePhoneNumberAsync(phoneNumber, ct);
        }
        catch (SmsProviderException)
        {
            // The provider could not give a verdict: do not store an unverified number.
            throw;
        }

        if (!validation.IsValid || string.IsNullOrEmpty(validation.CanonicalNumber))
        {
            return new ContactNumberRegistrationResult(null,
                validation.Reason ?? "The provider does not consider this a usable destination.", false);
        }

        var existing = await _contactNumberRepository.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndPhoneSpecification(buyerId, validation.CanonicalNumber), ct);
        if (existing != null)
        {
            return new ContactNumberRegistrationResult(null, "This number is already registered.", true);
        }

        var contactNumber = new ContactNumber(buyerId, validation.CanonicalNumber);
        await _contactNumberRepository.AddAsync(contactNumber, ct);
        _logger.LogInformation("Registered contact number {ContactNumberId} for buyer.", contactNumber.Id);
        return new ContactNumberRegistrationResult(contactNumber, null, false);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken ct = default)
    {
        return await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), ct);
    }

    public async Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken ct = default)
    {
        var contactNumber = await _contactNumberRepository.FirstOrDefaultAsync(
            new ContactNumberByIdAndBuyerSpecification(contactNumberId, buyerId), ct);
        if (contactNumber == null)
        {
            return false;
        }

        await _contactNumberRepository.DeleteAsync(contactNumber, ct);
        _logger.LogInformation("Deleted contact number {ContactNumberId} for buyer.", contactNumberId);
        return true;
    }
}
