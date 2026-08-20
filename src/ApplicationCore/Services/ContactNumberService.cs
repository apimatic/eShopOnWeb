using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly ITwilioLookupClient _lookupClient;
    private readonly IOrderNotificationService _orderNotificationService;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ContactNumber> contactNumbers,
        ITwilioLookupClient lookupClient,
        IOrderNotificationService orderNotificationService,
        IAppLogger<ContactNumberService> logger)
    {
        _contactNumbers = contactNumbers;
        _lookupClient = lookupClient;
        _orderNotificationService = orderNotificationService;
        _logger = logger;
    }

    public async Task<ContactNumber> RegisterAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw new InvalidContactNumberException("A mobile number is required.");
        }

        PhoneNumberLookupResult lookup;
        try
        {
            lookup = await _lookupClient.LookupAsync(phoneNumber.Trim(), cancellationToken);
        }
        catch (InvalidOperationException)
        {
            throw new InvalidContactNumberException("The messaging provider could not validate the number.");
        }

        if (!lookup.Valid || string.IsNullOrWhiteSpace(lookup.CanonicalPhoneNumber))
        {
            throw new InvalidContactNumberException("The messaging provider does not consider this number a usable destination.");
        }

        var duplicateSpec = new ContactNumberByBuyerAndPhoneSpecification(buyerId, lookup.CanonicalPhoneNumber);
        var existing = await _contactNumbers.FirstOrDefaultAsync(duplicateSpec, cancellationToken);
        if (existing is not null)
        {
            throw new DuplicateException("This number is already registered.");
        }

        var contactNumber = new ContactNumber(buyerId, lookup.CanonicalPhoneNumber);
        await _contactNumbers.AddAsync(contactNumber, cancellationToken);
        _logger.LogInformation("Registered contact number {ContactNumberId} for buyer {BuyerId}.", contactNumber.Id, buyerId);
        return contactNumber;
    }

    public async Task<IReadOnlyList<ContactNumber>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var spec = new ContactNumbersByBuyerSpecification(buyerId);
        return await _contactNumbers.ListAsync(spec, cancellationToken);
    }

    public async Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        var contactNumber = await _contactNumbers.GetByIdAsync(contactNumberId, cancellationToken);
        if (contactNumber is null || contactNumber.BuyerId != buyerId)
        {
            throw new OrderNotificationException("Contact number was not found.", 404);
        }

        var destination = contactNumber.PhoneNumber;
        await _contactNumbers.DeleteAsync(contactNumber, cancellationToken);
        await _orderNotificationService.CancelScheduledForDestinationAsync(destination, cancellationToken);
        _logger.LogInformation("Deleted contact number {ContactNumberId} for buyer {BuyerId}.", contactNumberId, buyerId);
    }
}
