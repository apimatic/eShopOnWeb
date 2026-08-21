using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IPhoneNumberLookup _lookup;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ContactNumber> contactNumbers,
        IPhoneNumberLookup lookup,
        IAppLogger<ContactNumberService> logger)
    {
        _contactNumbers = contactNumbers;
        _lookup = lookup;
        _logger = logger;
    }

    public async Task<ContactNumber> RegisterAsync(string buyerId, string phoneNumber, string? countryCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw new InvalidContactNumberException("A phone number is required.");
        }

        var lookup = await _lookup.LookupAsync(phoneNumber.Trim(), countryCode, cancellationToken);
        if (!lookup.Valid || string.IsNullOrWhiteSpace(lookup.CanonicalPhoneNumber))
        {
            var reason = lookup.ValidationErrors.Count > 0
                ? string.Join(", ", lookup.ValidationErrors)
                : "not a usable destination";
            throw new InvalidContactNumberException($"The number is not a usable destination: {reason}.");
        }

        var existing = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndPhoneSpecification(buyerId, lookup.CanonicalPhoneNumber), cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        var contact = new ContactNumber(buyerId, lookup.CanonicalPhoneNumber, lookup.NationalFormat);
        await _contactNumbers.AddAsync(contact, cancellationToken);
        _logger.LogInformation("Registered a contact number for buyer {BuyerId} with id {ContactNumberId}", buyerId, contact.Id);
        return contact;
    }

    public async Task<IReadOnlyList<ContactNumber>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return numbers;
    }

    public async Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        var contact = await _contactNumbers.GetByIdAsync(contactNumberId, cancellationToken);
        if (contact == null || contact.BuyerId != buyerId)
        {
            throw new KeyNotFoundException("Contact number was not found.");
        }

        await _contactNumbers.DeleteAsync(contact, cancellationToken);
        _logger.LogInformation("Removed contact number {ContactNumberId} for buyer {BuyerId}", contactNumberId, buyerId);
    }
}
