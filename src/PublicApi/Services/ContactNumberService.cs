using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.Twilio;

namespace Microsoft.eShopWeb.PublicApi.Services;

public interface IContactNumberService
{
    Task<RegisterContactNumberResult> RegisterAsync(string buyerId, string phoneNumber, CancellationToken ct);
    Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken ct);

    /// <summary>Removes a number owned by the buyer. Returns false when not found (or not owned).</summary>
    Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken ct);
}

public enum RegisterContactNumberStatus
{
    Registered,
    AlreadyRegistered,
    InvalidNumber
}

public sealed record RegisterContactNumberResult(
    RegisterContactNumberStatus Status,
    ContactNumber? ContactNumber,
    IReadOnlyList<string> ValidationErrors);

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly ITwilioMessaging _twilioMessaging;

    public ContactNumberService(IRepository<ContactNumber> contactNumberRepository, ITwilioMessaging twilioMessaging)
    {
        _contactNumberRepository = contactNumberRepository;
        _twilioMessaging = twilioMessaging;
    }

    public async Task<RegisterContactNumberResult> RegisterAsync(string buyerId, string phoneNumber, CancellationToken ct)
    {
        // Reject unusable destinations at registration time, not when a send fails.
        var validated = await _twilioMessaging.ValidatePhoneNumberAsync(phoneNumber, ct);
        if (!validated.IsValid || validated.CanonicalNumber is null)
        {
            return new RegisterContactNumberResult(
                RegisterContactNumberStatus.InvalidNumber,
                null,
                validated.ValidationErrors);
        }

        // Store the provider's canonical form, not what the caller typed.
        var existing = await _contactNumberRepository.ListAsync(new ContactNumbersForBuyerSpecification(buyerId), ct);
        var alreadyRegistered = existing.FirstOrDefault(c => c.PhoneNumber == validated.CanonicalNumber);
        if (alreadyRegistered is not null)
        {
            return new RegisterContactNumberResult(RegisterContactNumberStatus.AlreadyRegistered, alreadyRegistered, System.Array.Empty<string>());
        }

        var contactNumber = await _contactNumberRepository.AddAsync(new ContactNumber(buyerId, validated.CanonicalNumber), ct);
        return new RegisterContactNumberResult(RegisterContactNumberStatus.Registered, contactNumber, System.Array.Empty<string>());
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken ct)
    {
        return await _contactNumberRepository.ListAsync(new ContactNumbersForBuyerSpecification(buyerId), ct);
    }

    public async Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken ct)
    {
        var contactNumber = await _contactNumberRepository.GetByIdAsync(contactNumberId, ct);
        if (contactNumber is null || contactNumber.BuyerId != buyerId)
        {
            return false;
        }

        await _contactNumberRepository.DeleteAsync(contactNumber, ct);
        return true;
    }
}
