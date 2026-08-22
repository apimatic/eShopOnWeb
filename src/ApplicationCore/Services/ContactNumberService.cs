using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ShopperContactNumber> _contactNumbers;
    private readonly ISmsProvider _smsProvider;
    private readonly IOrderNotificationService _notifications;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ShopperContactNumber> contactNumbers,
        ISmsProvider smsProvider,
        IOrderNotificationService notifications,
        IAppLogger<ContactNumberService> logger)
    {
        _contactNumbers = contactNumbers;
        _smsProvider = smsProvider;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task<ShopperContactNumber> RegisterAsync(string buyerId, string rawNumber, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawNumber))
            throw new UnusablePhoneNumberException("A mobile number is required.");

        var lookup = await _smsProvider.LookupAsync(rawNumber, cancellationToken);
        if (!lookup.IsUsable || string.IsNullOrWhiteSpace(lookup.CanonicalNumber))
            throw new UnusablePhoneNumberException(lookup.RejectionReason ?? "The number is not a usable destination.");

        var existing = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndCanonicalSpec(buyerId, lookup.CanonicalNumber), cancellationToken);
        if (existing is not null)
            return existing;

        var created = new ShopperContactNumber(buyerId, lookup.CanonicalNumber);
        return await _contactNumbers.AddAsync(created, cancellationToken);
    }

    public async Task<IReadOnlyList<ShopperContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken)
    {
        return await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpec(buyerId), cancellationToken);
    }

    public async Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken)
    {
        var existing = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndIdSpec(buyerId, contactNumberId), cancellationToken);
        if (existing is null)
            throw new ContactNumberNotFoundException(contactNumberId);

        var canonical = existing.CanonicalNumber;
        await _contactNumbers.DeleteAsync(existing, cancellationToken);
        _logger.LogInformation("Removed contact number {ContactNumberId} for buyer {BuyerId}", contactNumberId, buyerId);

        await _notifications.CancelPendingFollowUpsForDestinationAsync(buyerId, canonical, cancellationToken);
    }
}
