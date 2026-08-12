using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Flow 1 — registers, lists and removes a shopper's contact numbers. A number is validated with the
/// provider up front and stored in the provider's canonical form; every operation is scoped to the
/// owning shopper. The number itself is never written to logs.
/// </summary>
public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly ISmsProvider _smsProvider;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ContactNumber> contactNumbers,
        ISmsProvider smsProvider,
        IAppLogger<ContactNumberService> logger)
    {
        _contactNumbers = contactNumbers;
        _smsProvider = smsProvider;
        _logger = logger;
    }

    public async Task<Result<ContactNumber>> RegisterAsync(string buyerId, string rawNumber, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        if (string.IsNullOrWhiteSpace(rawNumber))
        {
            return Result<ContactNumber>.Invalid(new List<ValidationError>
            {
                new() { Identifier = "phoneNumber", ErrorMessage = "A phone number is required." }
            });
        }

        var validation = await _smsProvider.ValidateNumberAsync(rawNumber, cancellationToken);
        if (!validation.IsValid || string.IsNullOrEmpty(validation.CanonicalE164))
        {
            var reason = validation.Errors.Count > 0
                ? string.Join(", ", validation.Errors)
                : "The provider does not consider this a usable destination.";
            // Note: the rejected number is deliberately not included in the log or the message.
            _logger.LogInformation("Rejected a contact number registration for buyer as not a usable destination.");
            return Result<ContactNumber>.Invalid(new List<ValidationError>
            {
                new() { Identifier = "phoneNumber", ErrorMessage = $"The phone number was rejected: {reason}" }
            });
        }

        var canonical = validation.CanonicalE164!;

        // Avoid registering the same canonical number twice for the same shopper.
        var existing = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndValueSpecification(buyerId, canonical), cancellationToken);
        if (existing is not null)
        {
            return Result<ContactNumber>.Success(existing);
        }

        var contactNumber = new ContactNumber(buyerId, canonical);
        await _contactNumbers.AddAsync(contactNumber, cancellationToken);
        _logger.LogInformation("Registered a contact number (id {ContactNumberId}) for a shopper.", contactNumber.Id);
        return Result<ContactNumber>.Success(contactNumber);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task<bool> RemoveAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var contactNumber = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByIdForBuyerSpecification(contactNumberId, buyerId), cancellationToken);
        if (contactNumber is null)
        {
            return false;
        }

        await _contactNumbers.DeleteAsync(contactNumber, cancellationToken);
        _logger.LogInformation("Removed contact number (id {ContactNumberId}) for a shopper.", contactNumberId);
        return true;
    }
}
