using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages the mobile numbers a shopper has on file. Every operation is scoped to the calling
/// shopper's own numbers.
/// </summary>
public interface IContactNumberService
{
    /// <summary>
    /// Registers a number for the shopper after the provider confirms it is a usable destination,
    /// storing the provider's canonical form. A number the provider will not accept is rejected here.
    /// </summary>
    Task<RegisterContactNumberResult> RegisterAsync(string ownerId, string rawNumber, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ContactNumber>> ListAsync(string ownerId, CancellationToken cancellationToken = default);

    /// <summary>Removes one of the shopper's numbers. Returns false if it is not theirs or not found.</summary>
    Task<bool> DeleteAsync(string ownerId, int contactNumberId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Outcome of a registration attempt. <see cref="Accepted"/> is false with a <see cref="Rejection"/>
/// reason when the provider does not consider the number a usable destination.
/// </summary>
public record RegisterContactNumberResult(bool Accepted, ContactNumber? ContactNumber, string? Rejection)
{
    public static RegisterContactNumberResult Rejected(string reason) => new(false, null, reason);
    public static RegisterContactNumberResult Ok(ContactNumber number) => new(true, number, null);
}
