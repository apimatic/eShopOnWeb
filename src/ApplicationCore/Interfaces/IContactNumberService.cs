using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages a shopper's registered contact numbers. Every operation is scoped to the caller
/// (<paramref name="buyerId"/>): one shopper can never see, use, or delete another's numbers.
/// </summary>
public interface IContactNumberService
{
    /// <summary>
    /// Registers a mobile number for the shopper after checking with the provider that it is a usable
    /// destination. What gets stored is the provider's canonical form of the number.
    /// </summary>
    Task<RegisterContactNumberResult> RegisterAsync(string buyerId, string rawNumber, CancellationToken ct = default);

    Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken ct = default);

    /// <summary>Removes one of the caller's numbers. Returns false if it is not the caller's / does not exist.</summary>
    Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken ct = default);
}

/// <summary>
/// Outcome of a registration attempt. A number the provider does not consider usable is rejected here,
/// with <see cref="Succeeded"/> false, rather than at the moment a later message fails to go out.
/// </summary>
public record RegisterContactNumberResult(bool Succeeded, int ContactNumberId, string? Error)
{
    public static RegisterContactNumberResult Ok(int id) => new(true, id, null);
    public static RegisterContactNumberResult Rejected(string error) => new(false, 0, error);
}
