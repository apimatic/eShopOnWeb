using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages the mobile numbers a shopper has on file. Every operation is scoped to a single shopper: one
/// shopper never sees, uses, or deletes another's number.
/// </summary>
public interface IContactNumberService
{
    /// <summary>
    /// Registers a number for the shopper. The number is validated and canonicalized with the provider
    /// first; an unusable number is rejected here (not at send time) and the provider's canonical form is
    /// what gets stored.
    /// </summary>
    Task<ContactNumberRegistration> RegisterAsync(string ownerId, string rawNumber, CancellationToken ct = default);

    /// <summary>The shopper's registered numbers.</summary>
    Task<IReadOnlyList<ContactNumber>> ListAsync(string ownerId, CancellationToken ct = default);

    /// <summary>
    /// Removes one of the shopper's numbers. Returns false when no such number belongs to this shopper.
    /// Afterwards the number no longer appears among the caller's numbers and nothing is sent to it again.
    /// </summary>
    Task<bool> DeleteAsync(string ownerId, int contactNumberId, CancellationToken ct = default);
}

/// <summary>
/// Outcome of a registration attempt. <see cref="Error"/> is a caller-safe reason (never echoing the
/// number) present only when <see cref="Succeeded"/> is false.
/// </summary>
public record ContactNumberRegistration(bool Succeeded, ContactNumber? ContactNumber, string? Error);
