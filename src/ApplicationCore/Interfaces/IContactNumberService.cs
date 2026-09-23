using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Flow 1 — a shopper's mobile contact numbers, scoped to the owning shopper.</summary>
public interface IContactNumberService
{
    /// <summary>
    /// Registers a number for the shopper after the provider confirms it is a usable destination.
    /// The provider's canonical form is what gets stored.
    /// </summary>
    Task<RegisterContactNumberResult> RegisterAsync(string ownerId, string phoneNumber, CancellationToken ct);

    /// <summary>The caller's registered numbers.</summary>
    Task<IReadOnlyList<ContactNumber>> ListAsync(string ownerId, CancellationToken ct);

    /// <summary>Removes one of the caller's numbers. Returns false if it is not the caller's or does not exist.</summary>
    Task<bool> DeleteAsync(string ownerId, int contactNumberId, CancellationToken ct);
}

public enum RegisterContactNumberOutcome
{
    Registered,
    RejectedUnusable,
    ProviderUnavailable
}

public record RegisterContactNumberResult(RegisterContactNumberOutcome Outcome, int? ContactNumberId,
    string? CanonicalNumber, string? Message);
