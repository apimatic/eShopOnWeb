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

    public async Task<ContactNumber> RegisterAsync(string buyerId, string phoneNumber, CancellationToken ct = default)
    {
        var validation = await _smsService.ValidatePhoneNumberAsync(phoneNumber, ct);
        if (!validation.IsValid || string.IsNullOrEmpty(validation.CanonicalNumber))
        {
            // Never log the rejected number itself — it is a shopper's personal detail.
            _logger.LogInformation("Rejected an unusable phone number for buyer {buyerId}: {error}", buyerId, validation.Error ?? "invalid number");
            throw new InvalidPhoneNumberException(
                $"The phone number is not a usable SMS destination: {validation.Error ?? "invalid number"}");
        }

        var contactNumber = new ContactNumber(buyerId, validation.CanonicalNumber);
        return await _contactNumberRepository.AddAsync(contactNumber, ct);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken ct = default)
    {
        return await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), ct);
    }

    public async Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken ct = default)
    {
        var contactNumber = await _contactNumberRepository.GetByIdAsync(contactNumberId, ct);
        if (contactNumber is null || contactNumber.BuyerId != buyerId)
        {
            return false;
        }

        await _contactNumberRepository.DeleteAsync(contactNumber, ct);
        return true;
    }
}
