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
    private readonly ISmsProvider _provider;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ContactNumber> repository,
        ISmsProvider provider,
        IAppLogger<ContactNumberService> logger)
    {
        _repository = repository;
        _provider = provider;
        _logger = logger;
    }

    public async Task<ContactNumberRegistration> RegisterAsync(string buyerId, string rawNumber, string? countryCode, CancellationToken cancellationToken = default)
    {
        // Validate at the edge, once, with the provider. Reject an unusable destination here rather than
        // at send time, and store the provider's canonical form rather than whatever was typed.
        var lookup = await _provider.LookupAsync(rawNumber, countryCode, cancellationToken);
        if (!lookup.Valid || string.IsNullOrEmpty(lookup.PhoneNumber))
        {
            var errors = lookup.ValidationErrors.Count > 0
                ? lookup.ValidationErrors
                : new[] { "NOT_A_USABLE_DESTINATION" };
            _logger.LogInformation($"Rejected a contact number for a buyer as not a usable destination ({string.Join(",", errors)}).");
            return new ContactNumberRegistration(false, null, errors);
        }

        var existing = await _repository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        var duplicate = existing.FirstOrDefault(c => c.PhoneNumber == lookup.PhoneNumber);
        if (duplicate is not null)
        {
            // Registering the same number twice is idempotent.
            return new ContactNumberRegistration(true, duplicate, Array.Empty<string>());
        }

        var contactNumber = new ContactNumber(buyerId, lookup.PhoneNumber, lookup.NationalFormat);
        await _repository.AddAsync(contactNumber, cancellationToken);
        _logger.LogInformation($"Registered a contact number (id {contactNumber.Id}) for a buyer.");
        return new ContactNumberRegistration(true, contactNumber, Array.Empty<string>());
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _repository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        // Scope by buyer so one shopper can never delete another's number.
        var contactNumber = await _repository.FirstOrDefaultAsync(
            new ContactNumberByIdForBuyerSpecification(buyerId, contactNumberId), cancellationToken);
        if (contactNumber is null)
        {
            return false;
        }

        await _repository.DeleteAsync(contactNumber, cancellationToken);
        _logger.LogInformation($"Removed contact number id {contactNumberId} for a buyer.");
        return true;
    }
}
