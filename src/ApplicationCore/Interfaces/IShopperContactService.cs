using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Shopper-scoped management of the mobile numbers a shopper puts on file. Every operation acts only on the
/// given buyer's own numbers.
/// </summary>
public interface IShopperContactService
{
    /// <summary>
    /// Validate a number with the provider and, if it is a usable destination, store its canonical form.
    /// A number the provider does not consider usable is rejected here (<see cref="ContactRegistrationResult.IsValidNumber"/> false).
    /// </summary>
    Task<ContactRegistrationResult> RegisterAsync(string buyerId, string phoneNumber, CancellationToken ct);

    /// <summary>The caller's registered numbers.</summary>
    Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken ct);

    /// <summary>Remove one of the caller's numbers. Returns false if it is not the caller's / does not exist.</summary>
    Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken ct);
}

public sealed record ContactRegistrationResult(bool IsValidNumber, int ContactNumberId, string? CanonicalNumber);
