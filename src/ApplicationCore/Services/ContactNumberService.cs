using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _repository;
    private readonly ITwilioMessagingClient _twilio;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(IRepository<ContactNumber> repository, ITwilioMessagingClient twilio,
        IAppLogger<ContactNumberService> logger)
    {
        _repository = repository;
        _twilio = twilio;
        _logger = logger;
    }

    public async Task<RegisterContactNumberResult> RegisterAsync(string ownerId, string rawNumber)
    {
        if (string.IsNullOrWhiteSpace(rawNumber))
        {
            return new RegisterContactNumberResult(ActionOutcome.BadRequest, 0, null, "A phone number is required.");
        }

        // Validate with the provider up front and store its canonical form — not what was typed.
        var lookup = await _twilio.LookupNumberAsync(rawNumber.Trim());
        if (!lookup.Valid || string.IsNullOrEmpty(lookup.PhoneNumberE164))
        {
            _logger.LogInformation("Rejected contact number registration for owner (provider reported not usable).");
            return new RegisterContactNumberResult(ActionOutcome.BadRequest, 0, null,
                "The number is not a usable destination.");
        }

        var canonical = lookup.PhoneNumberE164;

        // Idempotent: registering the same number twice returns the existing registration.
        var existing = await _repository.ListAsync(new ContactNumbersByOwnerSpecification(ownerId));
        var already = existing.FirstOrDefault(c => c.PhoneNumber == canonical);
        if (already is not null)
        {
            return new RegisterContactNumberResult(ActionOutcome.Ok, already.Id, already.PhoneNumber, null);
        }

        var contactNumber = new ContactNumber(ownerId, canonical);
        await _repository.AddAsync(contactNumber);
        _logger.LogInformation("Registered contact number id={Id} for owner.", contactNumber.Id);

        return new RegisterContactNumberResult(ActionOutcome.Ok, contactNumber.Id, contactNumber.PhoneNumber, null);
    }

    public async Task<IReadOnlyList<ContactNumberView>> ListAsync(string ownerId)
    {
        var numbers = await _repository.ListAsync(new ContactNumbersByOwnerSpecification(ownerId));
        return numbers
            .Select(c => new ContactNumberView(c.Id, c.PhoneNumber, c.CreatedDate))
            .ToList();
    }

    public async Task<bool> DeleteAsync(string ownerId, int contactNumberId)
    {
        // Scoped by owner: a number that belongs to someone else is simply "not found" here.
        var contactNumber = await _repository.FirstOrDefaultAsync(
            new ContactNumberByIdForOwnerSpecification(ownerId, contactNumberId));
        if (contactNumber is null)
        {
            return false;
        }

        await _repository.DeleteAsync(contactNumber);
        _logger.LogInformation("Deleted contact number id={Id} for owner.", contactNumberId);
        return true;
    }
}
