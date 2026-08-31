using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IPhoneNumberValidator _phoneNumberValidator;
    private readonly ISmsService _smsService;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ContactNumber> contactNumberRepository,
        IRepository<OrderNotification> notificationRepository,
        IPhoneNumberValidator phoneNumberValidator,
        ISmsService smsService,
        IAppLogger<ContactNumberService> logger)
    {
        _contactNumberRepository = contactNumberRepository;
        _notificationRepository = notificationRepository;
        _phoneNumberValidator = phoneNumberValidator;
        _smsService = smsService;
        _logger = logger;
    }

    public async Task<ContactNumberRegistrationResult> RegisterAsync(string buyerId, string rawNumber, string? countryCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawNumber))
        {
            return ContactNumberRegistrationResult.Failed("A phone number is required.");
        }

        var validation = await _phoneNumberValidator.ValidateAsync(rawNumber.Trim(), countryCode, cancellationToken);
        if (!validation.IsValid || validation.E164Number is null)
        {
            return ContactNumberRegistrationResult.Failed(validation.Errors.Count > 0
                ? validation.Errors.ToArray()
                : new[] { "The provider does not consider this a usable destination." });
        }

        var existing = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpec(buyerId), cancellationToken);
        var duplicate = existing.Find(c => c.PhoneNumber == validation.E164Number);
        if (duplicate is not null)
        {
            return new ContactNumberRegistrationResult(duplicate, new List<string>());
        }

        var contactNumber = new ContactNumber(buyerId, validation.E164Number, validation.NationalFormat);
        await _contactNumberRepository.AddAsync(contactNumber, cancellationToken);

        return new ContactNumberRegistrationResult(contactNumber, new List<string>());
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpec(buyerId), cancellationToken);
    }

    public async Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        var contactNumber = await _contactNumberRepository.GetByIdAsync(contactNumberId, cancellationToken);
        if (contactNumber is null || contactNumber.BuyerId != buyerId)
        {
            return false;
        }

        // Nothing may be sent to the number again: call off anything the provider
        // is still holding for it before removing the record.
        var scheduled = await _notificationRepository.ListAsync(new ScheduledNotificationsForContactSpec(contactNumberId), cancellationToken);
        foreach (var notification in scheduled)
        {
            if (notification.ProviderMessageSid is null)
            {
                continue;
            }

            var cancelled = await _smsService.CancelScheduledAsync(notification.ProviderMessageSid, cancellationToken);
            if (cancelled)
            {
                notification.UpdateStatus(NotificationStatuses.Canceled, null);
                await _notificationRepository.UpdateAsync(notification, cancellationToken);
            }
            else
            {
                _logger.LogWarning("Could not cancel scheduled notification {NotificationId} while removing its destination number.", notification.Id);
            }
        }

        await _contactNumberRepository.DeleteAsync(contactNumber, cancellationToken);
        return true;
    }
}
