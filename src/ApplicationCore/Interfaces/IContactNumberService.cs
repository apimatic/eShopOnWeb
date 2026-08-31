using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IContactNumberService
{
    /// <summary>Validates a number with the provider and registers the provider's canonical form.</summary>
    Task<ContactNumberRegistrationResult> RegisterAsync(string buyerId, string phoneNumber, CancellationToken ct = default);
    Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken ct = default);
    /// <summary>Removes a number owned by the buyer. Returns false when no such number exists.</summary>
    Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken ct = default);
}

public record ContactNumberRegistrationResult(ContactNumber? ContactNumber, string? Error, bool IsDuplicate)
{
    public bool Success => ContactNumber != null;
}
