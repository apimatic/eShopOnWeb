using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _repository;
    private readonly ISmsGateway _smsGateway;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(IRepository<ContactNumber> repository, ISmsGateway smsGateway,
        IAppLogger<ContactNumberService> logger)
    {
        _repository = repository;
        _smsGateway = smsGateway;
        _logger = logger;
    }

    public async Task<ContactNumberView> RegisterAsync(string buyerId, string rawPhoneNumber,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrWhiteSpace(rawPhoneNumber, nameof(rawPhoneNumber));

        // Validate up front with the provider so an unusable destination is rejected here rather than
        // when a message later fails to go out. The provider's canonical form is what we keep.
        var validation = await _smsGateway.ValidateNumberAsync(rawPhoneNumber, cancellationToken);
        if (!validation.IsValid || string.IsNullOrEmpty(validation.CanonicalNumber))
        {
            // Deliberately no phone number in the message — the number is never written to logs or errors.
            var reason = validation.ValidationErrors.Any()
                ? $" ({string.Join(", ", validation.ValidationErrors)})"
                : string.Empty;
            throw new InvalidPhoneNumberException($"The phone number provided is not a valid, reachable destination.{reason}");
        }

        var canonical = validation.CanonicalNumber;

        // Registering the same number twice for the same shopper is a no-op returning the existing one.
        var existing = await _repository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        var already = existing.FirstOrDefault(c => c.PhoneNumber == canonical);
        if (already is not null)
        {
            return ToView(already);
        }

        var contactNumber = new ContactNumber(buyerId, canonical);
        await _repository.AddAsync(contactNumber, cancellationToken);

        _logger.LogInformation("Registered contact number {ContactNumberId}.", contactNumber.Id);
        return ToView(contactNumber);
    }

    public async Task<IReadOnlyList<ContactNumberView>> ListAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        var numbers = await _repository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return numbers.Select(ToView).ToList();
    }

    public async Task<bool> DeleteAsync(string buyerId, int contactNumberId,
        CancellationToken cancellationToken = default)
    {
        // Scoped by buyer: a caller can only ever find (and delete) their own number.
        var contactNumber = await _repository.FirstOrDefaultAsync(
            new ContactNumberByIdAndBuyerSpecification(contactNumberId, buyerId), cancellationToken);
        if (contactNumber is null)
        {
            return false;
        }

        await _repository.DeleteAsync(contactNumber, cancellationToken);
        _logger.LogInformation("Deleted contact number {ContactNumberId}.", contactNumberId);
        return true;
    }

    private static ContactNumberView ToView(ContactNumber c) =>
        new(c.Id, c.PhoneNumber, c.CreatedAt);
}
