using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages a shopper's registered mobile contact numbers. Every operation is scoped to the owning
/// shopper — one shopper can never see, use or delete another's number.
/// </summary>
public interface IContactNumberService
{
    /// <summary>
    /// Register a number for a shopper. The number is validated with the provider first; an unusable
    /// destination is rejected here. The provider's canonical form of the number is what gets stored.
    /// Throws <see cref="Exceptions.InvalidPhoneNumberException"/> if the provider does not consider it usable.
    /// </summary>
    Task<ContactNumberView> RegisterAsync(string buyerId, string rawPhoneNumber, CancellationToken cancellationToken = default);

    /// <summary>The caller's own registered numbers.</summary>
    Task<IReadOnlyList<ContactNumberView>> ListAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove one of the caller's numbers. Returns false if no such number belongs to the caller
    /// (so a caller can never delete another shopper's number). Afterwards nothing is sent to it again.
    /// </summary>
    Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default);
}
