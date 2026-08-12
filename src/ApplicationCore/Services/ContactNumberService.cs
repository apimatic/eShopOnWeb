using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly ITwilioMessagingClient _messaging;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ContactNumber> contactNumbers,
        ITwilioMessagingClient messaging,
        IAppLogger<ContactNumberService> logger)
    {
        _contactNumbers = contactNumbers;
        _messaging = messaging;
        _logger = logger;
    }

    public async Task<Result<int>> RegisterAsync(string buyerId, string rawNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawNumber))
        {
            return Result<int>.Invalid(new ValidationError
            {
                Identifier = "phoneNumber",
                ErrorMessage = "A phone number is required."
            });
        }

        // Ask the provider whether this is a usable destination and for its canonical form.
        var lookup = await _messaging.LookupAsync(rawNumber, cancellationToken);
        if (!lookup.Valid || string.IsNullOrEmpty(lookup.PhoneNumber))
        {
            var reason = lookup.ValidationErrors.Count > 0
                ? string.Join(", ", lookup.ValidationErrors)
                : "not a valid, reachable destination";
            _logger.LogInformation("Contact-number registration for buyer {BuyerId} rejected by provider validation.", buyerId);
            return Result<int>.Invalid(new ValidationError
            {
                Identifier = "phoneNumber",
                ErrorMessage = $"The number could not be registered ({reason})."
            });
        }

        var canonical = lookup.PhoneNumber;

        // A shopper registering the same number twice just gets the existing one back.
        var existing = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        var already = existing.FirstOrDefault(c => c.PhoneNumber == canonical);
        if (already is not null)
        {
            return Result<int>.Success(already.Id);
        }

        var contactNumber = new ContactNumber(buyerId, canonical);
        await _contactNumbers.AddAsync(contactNumber, cancellationToken);
        _logger.LogInformation("Registered contact number {ContactNumberId} for buyer {BuyerId}.", contactNumber.Id, buyerId);
        return Result<int>.Success(contactNumber.Id);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task<Result> DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        // Scope to the caller: another shopper's number is simply "not found" here.
        var owned = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        var target = owned.FirstOrDefault(c => c.Id == contactNumberId);
        if (target is null)
        {
            return Result.NotFound();
        }

        await _contactNumbers.DeleteAsync(target, cancellationToken);
        _logger.LogInformation("Deleted contact number {ContactNumberId} for buyer {BuyerId}.", contactNumberId, buyerId);
        return Result.Success();
    }
}
