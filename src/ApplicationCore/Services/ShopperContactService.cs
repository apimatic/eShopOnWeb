using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ShopperContactService : IShopperContactService
{
    private readonly IRepository<ShopperContactNumber> _contactRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly ISmsGateway _smsGateway;
    private readonly IAppLogger<ShopperContactService> _logger;

    public ShopperContactService(
        IRepository<ShopperContactNumber> contactRepository,
        IRepository<OrderNotification> notificationRepository,
        ISmsGateway smsGateway,
        IAppLogger<ShopperContactService> logger)
    {
        _contactRepository = contactRepository;
        _notificationRepository = notificationRepository;
        _smsGateway = smsGateway;
        _logger = logger;
    }

    public async Task<ShopperContactNumber> RegisterAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(phoneNumber, nameof(phoneNumber));

        PhoneNumberLookupResult lookup;
        try
        {
            lookup = await _smsGateway.LookupAsync(phoneNumber, cancellationToken);
        }
        catch (SmsProviderException)
        {
            throw;
        }

        if (!lookup.IsUsable || string.IsNullOrEmpty(lookup.CanonicalNumber))
        {
            throw new InvalidContactNumberException();
        }

        var existing = await _contactRepository.ListAsync(new ContactNumbersByBuyerSpec(buyerId), cancellationToken);
        var duplicate = existing.FirstOrDefault(c => c.PhoneNumber == lookup.CanonicalNumber);
        if (duplicate != null)
        {
            return duplicate;
        }

        var contact = new ShopperContactNumber(buyerId, lookup.CanonicalNumber);
        return await _contactRepository.AddAsync(contact, cancellationToken);
    }

    public async Task<IReadOnlyList<ShopperContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _contactRepository.ListAsync(new ContactNumbersByBuyerSpec(buyerId), cancellationToken);
    }

    public async Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var contact = await _contactRepository.FirstOrDefaultAsync(
            new ContactNumberByIdAndBuyerSpec(contactNumberId, buyerId), cancellationToken);
        if (contact is null)
        {
            return false;
        }

        var scheduled = await _notificationRepository.ListAsync(
            new ScheduledFollowUpsByBuyerSpec(buyerId), cancellationToken);
        foreach (var followUp in scheduled)
        {
            try
            {
                var cancelled = await _smsGateway.CancelScheduledAsync(followUp.ProviderSid, cancellationToken);
                if (cancelled)
                {
                    followUp.ApplyProviderOutcome("canceled", null, null, followUp.Body);
                    await _notificationRepository.UpdateAsync(followUp, cancellationToken);
                }
            }
            catch (SmsProviderException)
            {
                _logger.LogWarning("Failed to cancel a scheduled follow-up when a contact number was removed. NotificationId {NotificationId}", followUp.Id);
            }
        }

        await _contactRepository.DeleteAsync(contact, cancellationToken);
        return true;
    }

    public async Task<string?> GetPrimaryNumberAsync(string buyerId, CancellationToken cancellationToken)
    {
        var numbers = await ListAsync(buyerId, cancellationToken);
        return numbers.FirstOrDefault()?.PhoneNumber;
    }
}
