using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.Twilio;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly ITwilioLookupClient _lookupClient;
    private readonly ILogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ContactNumber> contactNumbers,
        ITwilioLookupClient lookupClient,
        ILogger<ContactNumberService> logger)
    {
        _contactNumbers = contactNumbers;
        _lookupClient = lookupClient;
        _logger = logger;
    }

    public async Task<ContactNumber> RegisterAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken = default)
    {
        PhoneNumberLookupResult lookup;
        try
        {
            lookup = await _lookupClient.LookupAsync(phoneNumber, cancellationToken);
        }
        catch (TwilioClientException ex)
        {
            _logger.LogWarning("Phone number lookup failed with HTTP {StatusCode}.", ex.StatusCode);
            throw;
        }

        if (!lookup.Valid || string.IsNullOrWhiteSpace(lookup.CanonicalNumber))
        {
            throw new ContactNumberNotUsableException("The provider does not consider this number a usable destination.");
        }

        var existing = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndCanonicalSpecification(buyerId, lookup.CanonicalNumber),
            cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        var contact = new ContactNumber(buyerId, lookup.CanonicalNumber);
        return await _contactNumbers.AddAsync(contact, cancellationToken);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        var contact = await _contactNumbers.GetByIdAsync(contactNumberId, cancellationToken);
        if (contact == null || contact.BuyerId != buyerId)
        {
            throw new EntityNotFoundException(nameof(ContactNumber), contactNumberId);
        }

        await _contactNumbers.DeleteAsync(contact, cancellationToken);
    }
}
