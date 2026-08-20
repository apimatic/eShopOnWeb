using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly ISmsNotificationGateway _sms;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ContactNumber> contactNumbers,
        ISmsNotificationGateway sms,
        IAppLogger<ContactNumberService> logger)
    {
        _contactNumbers = contactNumbers;
        _sms = sms;
        _logger = logger;
    }

    public async Task<ContactNumber> RegisterAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(phoneNumber, nameof(phoneNumber));

        var lookup = await _sms.LookupAsync(phoneNumber.Trim(), cancellationToken);
        if (!lookup.IsUsable || string.IsNullOrWhiteSpace(lookup.CanonicalNumber))
        {
            throw new UnusableContactNumberException(
                lookup.RejectionReason ?? "The provider does not consider this a usable destination.");
        }

        var canonical = lookup.CanonicalNumber;
        var existing = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndE164Spec(buyerId, canonical), cancellationToken);
        if (existing is not null)
        {
            throw new DuplicateException("This mobile number is already registered.");
        }

        var contact = new ContactNumber(buyerId, canonical);
        await _contactNumbers.AddAsync(contact, cancellationToken);
        _logger.LogInformation("Registered contact number {ContactNumberId} for buyer {BuyerId}", contact.Id, buyerId);
        return contact;
    }

    public async Task<IReadOnlyList<ContactNumber>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpec(buyerId), cancellationToken);
    }

    public async Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var contact = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndIdSpec(buyerId, contactNumberId), cancellationToken);
        if (contact is null)
        {
            throw new EntityNotFoundException("Contact number was not found.");
        }

        await _contactNumbers.DeleteAsync(contact, cancellationToken);
        _logger.LogInformation("Removed contact number {ContactNumberId} for buyer {BuyerId}", contactNumberId, buyerId);
    }
}
