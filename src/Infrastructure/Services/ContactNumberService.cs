using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Twilio;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Manages a shopper's contact numbers. Registration validates the number with the provider up
/// front and stores the provider's canonical form. Every read/delete is scoped to the caller.
/// </summary>
public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly ITwilioLookupClient _lookup;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ContactNumber> contactNumbers,
        ITwilioLookupClient lookup,
        IAppLogger<ContactNumberService> logger)
    {
        _contactNumbers = contactNumbers;
        _lookup = lookup;
        _logger = logger;
    }

    public async Task<ContactNumberRegistrationResult> RegisterAsync(string buyerId, string rawNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawNumber))
        {
            return ContactNumberRegistrationResult.Rejected("A phone number is required.");
        }

        var lookup = await _lookup.LookupAsync(rawNumber.Trim(), cancellationToken);
        if (!lookup.Valid || string.IsNullOrWhiteSpace(lookup.PhoneNumber))
        {
            // Rejected here, at registration, rather than when a later message fails to go out.
            _logger.LogInformation("Rejected a contact-number registration for buyer {BuyerId}: not a usable destination.", buyerId);
            return ContactNumberRegistrationResult.Rejected("The number is not a valid, reachable mobile destination.");
        }

        var canonical = lookup.PhoneNumber!;

        // If the shopper already has this canonical number on file, return it rather than duplicating.
        var existing = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        var already = existing.FirstOrDefault(c => c.PhoneNumber == canonical);
        if (already != null)
        {
            return ContactNumberRegistrationResult.Success(already);
        }

        var contactNumber = new ContactNumber(buyerId, canonical);
        contactNumber = await _contactNumbers.AddAsync(contactNumber, cancellationToken);

        _logger.LogInformation("Registered contact number {ContactNumberId} for buyer {BuyerId}.", contactNumber.Id, buyerId);
        return ContactNumberRegistrationResult.Success(contactNumber);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return numbers;
    }

    public async Task<bool> RemoveAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        // Scope the lookup to the caller so one shopper can never delete another's number.
        var contactNumber = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerSpecification(buyerId, contactNumberId), cancellationToken);

        if (contactNumber == null)
        {
            return false;
        }

        await _contactNumbers.DeleteAsync(contactNumber, cancellationToken);
        _logger.LogInformation("Removed contact number {ContactNumberId} for buyer {BuyerId}.", contactNumberId, buyerId);
        return true;
    }
}
