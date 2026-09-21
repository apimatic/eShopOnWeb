using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _repository;
    private readonly ISmsGateway _smsGateway;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ContactNumber> repository,
        ISmsGateway smsGateway,
        IAppLogger<ContactNumberService> logger)
    {
        _repository = repository;
        _smsGateway = smsGateway;
        _logger = logger;
    }

    public async Task<ContactNumberRegistration> RegisterAsync(string buyerId, string phoneNumber, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return new ContactNumberRegistration(false, null, null, "A phone number is required.");
        }

        // Reject a number the provider does not consider usable at registration — not later when a
        // message fails. Store the provider's own canonical form, not what the caller typed.
        var validation = await _smsGateway.ValidateAndCanonicalizeAsync(phoneNumber, ct);
        if (!validation.IsValid || validation.CanonicalE164 is null)
        {
            _logger.LogInformation("Rejected a contact-number registration: number is not a usable destination.");
            return new ContactNumberRegistration(false, null, null,
                validation.Reason ?? "The number is not a usable destination.");
        }

        var contactNumber = new ContactNumber(buyerId, validation.CanonicalE164);
        contactNumber = await _repository.AddAsync(contactNumber, ct);

        _logger.LogInformation("Contact number {ContactNumberId} registered for a shopper.", contactNumber.Id);
        return new ContactNumberRegistration(true, contactNumber.Id, contactNumber.PhoneNumber, null);
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken ct) =>
        await _repository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), ct);

    public async Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken ct)
    {
        // Scoped by buyer: one shopper can never delete another's number.
        var contactNumber = await _repository.FirstOrDefaultAsync(
            new ContactNumberByIdForBuyerSpecification(contactNumberId, buyerId), ct);
        if (contactNumber is null)
        {
            return false;
        }

        await _repository.DeleteAsync(contactNumber, ct);
        _logger.LogInformation("Contact number {ContactNumberId} removed for a shopper.", contactNumberId);
        return true;
    }
}
