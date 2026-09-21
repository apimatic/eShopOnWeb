using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Outcome of registering a contact number.</summary>
public record RegisterContactNumberResult(bool Succeeded, ContactNumber? ContactNumber, string? Error)
{
    public static RegisterContactNumberResult Ok(ContactNumber c) => new(true, c, null);
    public static RegisterContactNumberResult Rejected(string error) => new(false, null, error);
}

public interface IContactNumberService
{
    /// <summary>
    /// Validate the raw number with the provider, reject it here if it is not a usable destination,
    /// and store the provider's canonical form for the given shopper.
    /// </summary>
    Task<RegisterContactNumberResult> RegisterAsync(string buyerId, string rawPhoneNumber, CancellationToken ct);

    Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken ct);

    /// <summary>Remove one of the caller's numbers. Returns false when it is not the caller's / not found.</summary>
    Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken ct);
}
