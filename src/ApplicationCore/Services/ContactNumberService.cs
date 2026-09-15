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
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IPhoneNumberValidator _validator;

    public ContactNumberService(IRepository<ContactNumber> contactNumbers, IPhoneNumberValidator validator)
    {
        _contactNumbers = contactNumbers;
        _validator = validator;
    }

    public async Task<ContactNumber> RegisterAsync(string ownerId, string rawNumber, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(ownerId, nameof(ownerId));
        Guard.Against.NullOrWhiteSpace(rawNumber, nameof(rawNumber));

        var validation = await _validator.ValidateAsync(rawNumber, cancellationToken);
        if (!validation.IsValid || string.IsNullOrEmpty(validation.CanonicalE164))
        {
            throw new InvalidPhoneNumberException(validation.Errors);
        }

        var canonical = validation.CanonicalE164!;

        // Registering the same number twice for the same shopper returns the existing record rather than
        // storing a duplicate that would be messaged twice.
        var owned = await _contactNumbers.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), cancellationToken);
        var existing = owned.FirstOrDefault(c => c.PhoneNumber == canonical);
        if (existing is not null)
            return existing;

        var contactNumber = new ContactNumber(ownerId, canonical);
        return await _contactNumbers.AddAsync(contactNumber, cancellationToken);
    }
}
