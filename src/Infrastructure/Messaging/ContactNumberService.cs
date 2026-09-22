using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>Registers and manages a shopper's contact numbers, always scoped to the owner.</summary>
public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _repository;
    private readonly IPhoneNumberValidator _validator;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(IRepository<ContactNumber> repository, IPhoneNumberValidator validator,
        IAppLogger<ContactNumberService> logger)
    {
        _repository = repository;
        _validator = validator;
        _logger = logger;
    }

    public async Task<int> RegisterAsync(string ownerId, string rawNumber, CancellationToken ct)
    {
        // Reject an unusable number here (at registration), and store the provider's canonical form.
        var validation = await _validator.ValidateAsync(rawNumber, ct);
        if (!validation.IsUsable || string.IsNullOrWhiteSpace(validation.CanonicalE164))
            throw new ContactNumberNotUsableException();

        var contactNumber = new ContactNumber(ownerId, validation.CanonicalE164);
        contactNumber = await _repository.AddAsync(contactNumber, ct);
        _logger.LogInformation("Registered contact number {Id} for a shopper.", contactNumber.Id);
        return contactNumber.Id;
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string ownerId, CancellationToken ct)
    {
        var numbers = await _repository.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), ct);
        return numbers;
    }

    public async Task<bool> RemoveAsync(string ownerId, int contactNumberId, CancellationToken ct)
    {
        // Owner-scoped fetch: another shopper's number is invisible here, so it cannot be deleted.
        var existing = await _repository.FirstOrDefaultAsync(
            new ContactNumberByIdForOwnerSpecification(contactNumberId, ownerId), ct);
        if (existing is null) return false;

        await _repository.DeleteAsync(existing, ct);
        _logger.LogInformation("Removed contact number {Id}.", contactNumberId);
        return true;
    }
}
