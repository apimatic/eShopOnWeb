using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _repository;
    private readonly ITwilioMessagingGateway _twilio;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ContactNumber> repository,
        ITwilioMessagingGateway twilio,
        IAppLogger<ContactNumberService> logger)
    {
        _repository = repository;
        _twilio = twilio;
        _logger = logger;
    }

    public async Task<ContactNumber> RegisterAsync(string buyerId, string rawNumber, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawNumber))
        {
            throw new InvalidContactNumberException("A mobile number is required.");
        }

        var lookup = await _twilio.LookupAsync(rawNumber.Trim(), cancellationToken);
        if (!lookup.IsUsable || string.IsNullOrWhiteSpace(lookup.CanonicalNumber))
        {
            throw new InvalidContactNumberException(
                lookup.FailureMessage ?? "The number is not a usable destination.");
        }

        var existing = await _repository.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndCanonicalSpecification(buyerId, lookup.CanonicalNumber),
            cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var contact = new ContactNumber(buyerId, lookup.CanonicalNumber);
        await _repository.AddAsync(contact, cancellationToken);
        _logger.LogInformation("Registered contact number {ContactNumberId}", contact.Id);
        return contact;
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken)
    {
        return await _repository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken)
    {
        var contact = await _repository.GetByIdAsync(contactNumberId, cancellationToken);
        if (contact is null || !contact.BelongsTo(buyerId))
        {
            throw new ContactNumberNotFoundException(contactNumberId);
        }

        await _repository.DeleteAsync(contact, cancellationToken);
        _logger.LogInformation("Removed contact number {ContactNumberId}", contactNumberId);
    }
}
