using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages a shopper's mobile numbers on file. Every operation is scoped to the owner: one shopper can
/// never see, use, or delete another's number.
/// </summary>
public interface IContactNumberService
{
    /// <summary>
    /// Validate a typed number with the provider and, if it is a usable destination, store the provider's
    /// canonical form for the owner. A number the provider rejects is not stored.
    /// </summary>
    Task<RegisterContactNumberResult> RegisterAsync(string ownerId, string rawPhoneNumber, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ContactNumber>> ListAsync(string ownerId, CancellationToken cancellationToken = default);

    /// <summary>Remove one of the owner's numbers. Returns false if it does not exist or is not theirs.</summary>
    Task<bool> RemoveAsync(string ownerId, int contactNumberId, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of registering a number. On failure <see cref="ContactNumber"/> is null.</summary>
public record RegisterContactNumberResult(
    bool Success, ContactNumber? ContactNumber, string? Error, IReadOnlyList<string> ValidationErrors)
{
    public static RegisterContactNumberResult Ok(ContactNumber contactNumber) =>
        new(true, contactNumber, null, new List<string>());

    public static RegisterContactNumberResult Rejected(string error, IReadOnlyList<string> validationErrors) =>
        new(false, null, error, validationErrors);
}
