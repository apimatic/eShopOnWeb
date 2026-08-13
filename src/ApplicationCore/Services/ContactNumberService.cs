using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _repository;
    private readonly IPhoneNumberValidator _validator;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ContactNumber> repository,
        IPhoneNumberValidator validator,
        IAppLogger<ContactNumberService> logger)
    {
        _repository = repository;
        _validator = validator;
        _logger = logger;
    }

    public async Task<ContactNumberRegistration> RegisterAsync(string buyerId, string rawNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawNumber))
        {
            return new ContactNumberRegistration(false, null, new[] { "A phone number is required." });
        }

        // Reject an unusable destination here, at registration, rather than when a send later fails.
        var validation = await _validator.ValidateAsync(rawNumber.Trim(), cancellationToken);
        if (!validation.IsValid || string.IsNullOrEmpty(validation.CanonicalNumber))
        {
            var errors = validation.Errors.Count > 0
                ? validation.Errors
                : new[] { "The number is not a usable messaging destination." };
            // Note: the offending number itself is never logged.
            _logger.LogWarning($"Rejected a contact number registration for buyer as not a usable destination.");
            return new ContactNumberRegistration(false, null, errors);
        }

        var canonical = validation.CanonicalNumber;

        // Store the provider's canonical form. Avoid duplicating a number the shopper already has.
        var existing = await _repository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        var alreadyOnFile = existing.FirstOrDefault(c => c.PhoneNumber == canonical);
        if (alreadyOnFile is not null)
        {
            return new ContactNumberRegistration(true, alreadyOnFile, Array.Empty<string>());
        }

        var contactNumber = new ContactNumber(buyerId, canonical);
        contactNumber = await _repository.AddAsync(contactNumber, cancellationToken);
        return new ContactNumberRegistration(true, contactNumber, Array.Empty<string>());
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _repository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        var contactNumber = await _repository.GetByIdAsync(contactNumberId, cancellationToken);

        // A number belongs to the shopper who registered it: one shopper can never delete another's.
        if (contactNumber is null || contactNumber.BuyerId != buyerId)
        {
            return false;
        }

        await _repository.DeleteAsync(contactNumber, cancellationToken);
        return true;
    }
}
