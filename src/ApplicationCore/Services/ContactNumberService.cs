using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly ISmsGateway _smsGateway;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ContactNumber> contactNumbers,
        ISmsGateway smsGateway,
        IAppLogger<ContactNumberService> logger)
    {
        _contactNumbers = contactNumbers;
        _smsGateway = smsGateway;
        _logger = logger;
    }

    public async Task<ContactNumber> RegisterAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(phoneNumber, nameof(phoneNumber));

        var lookup = await _smsGateway.LookupAsync(phoneNumber, cancellationToken);
        if (!lookup.Valid || string.IsNullOrEmpty(lookup.CanonicalE164))
        {
            var reason = lookup.ValidationErrors.Count > 0
                ? string.Join(", ", lookup.ValidationErrors)
                : "NOT_A_NUMBER";
            throw new InvalidContactNumberException(
                $"The provider does not consider this a usable destination ({reason}).",
                lookup.ValidationErrors);
        }

        var existing = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndE164Specification(buyerId, lookup.CanonicalE164),
            cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        var contact = new ContactNumber(buyerId, lookup.CanonicalE164, lookup.NationalFormat);
        await _contactNumbers.AddAsync(contact, cancellationToken);
        _logger.LogInformation("Registered contact number {ContactNumberId} for buyer {BuyerId}", contact.Id, buyerId);
        return contact;
    }

    public async Task<IReadOnlyList<ContactNumber>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var contact = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByIdAndBuyerSpecification(contactNumberId, buyerId),
            cancellationToken);
        if (contact == null)
        {
            return false;
        }

        await _contactNumbers.DeleteAsync(contact, cancellationToken);
        _logger.LogInformation("Removed contact number {ContactNumberId} for buyer {BuyerId}", contactNumberId, buyerId);
        return true;
    }
}
