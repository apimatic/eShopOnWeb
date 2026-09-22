using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages a shopper's on-file mobile numbers. Every operation is scoped to a single owner (the
/// caller's token identity): one shopper never sees, uses, or deletes another's number.
/// </summary>
public interface IContactNumberService
{
    /// <summary>
    /// Register a number for the owner. The provider validates it up front; a number the provider
    /// does not consider a usable destination is rejected here (returns
    /// <see cref="ContactNumberRegistration.Rejected"/>), not when a later message fails. What is
    /// stored is the provider's canonical form of the number.
    /// </summary>
    Task<ContactNumberRegistration> RegisterAsync(string ownerId, string rawNumber, CancellationToken ct);

    Task<IReadOnlyList<ContactNumber>> ListAsync(string ownerId, CancellationToken ct);

    /// <summary>Remove one of the owner's numbers. Returns false if it is not the owner's / not found.</summary>
    Task<bool> RemoveAsync(string ownerId, int contactNumberId, CancellationToken ct);
}

/// <summary>The outcome of a registration attempt.</summary>
public sealed record ContactNumberRegistration
{
    public bool Succeeded { get; init; }
    public ContactNumber? ContactNumber { get; init; }

    public static ContactNumberRegistration Ok(ContactNumber number) =>
        new() { Succeeded = true, ContactNumber = number };

    public static ContactNumberRegistration Rejected { get; } = new() { Succeeded = false };
}
