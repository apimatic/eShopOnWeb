using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Registers, lists and removes a shopper's mobile contact numbers. Every operation is scoped to the
/// calling shopper: one shopper never sees, uses or deletes another's number.
/// </summary>
public interface IContactNumberService
{
    Task<RegisterContactNumberResult> RegisterAsync(string ownerId, string rawPhoneNumber, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ContactNumber>> ListAsync(string ownerId, CancellationToken cancellationToken = default);

    /// <summary>Removes a number the caller owns. Returns false when the caller does not own such a number.</summary>
    Task<bool> DeleteAsync(string ownerId, int contactNumberId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Outcome of a registration attempt. A number the provider rejects as unusable comes back with
/// Succeeded = false so the caller can be told at registration time, not when a later message fails.
/// </summary>
public record RegisterContactNumberResult(bool Succeeded, ContactNumber? ContactNumber, string? Error)
{
    public static RegisterContactNumberResult Ok(ContactNumber number) => new(true, number, null);
    public static RegisterContactNumberResult Rejected(string error) => new(false, null, error);
}
