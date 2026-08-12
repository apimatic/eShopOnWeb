using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages the mobile numbers a shopper has on file. Every operation is scoped to the calling
/// shopper: a number belongs to whoever registered it, and no shopper can see, use or delete
/// another's.
/// </summary>
public interface IContactNumberService
{
    /// <summary>
    /// Register a number for a shopper. The number is validated with the provider first; one it does
    /// not consider a usable destination is rejected here, and the provider's canonical form is what
    /// gets stored.
    /// </summary>
    Task<RegisterContactNumberResult> RegisterAsync(string buyerId, string rawPhoneNumber, CancellationToken cancellationToken = default);

    /// <summary>The caller's registered numbers.</summary>
    Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove one of the caller's numbers. Returns false when the number does not exist or belongs to
    /// someone else.
    /// </summary>
    Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of a registration attempt.</summary>
public record RegisterContactNumberResult(bool Succeeded, ContactNumber? ContactNumber, string? Error)
{
    public static RegisterContactNumberResult Success(ContactNumber number) => new(true, number, null);
    public static RegisterContactNumberResult Rejected(string error) => new(false, null, error);
}
