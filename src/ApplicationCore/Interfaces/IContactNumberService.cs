using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// A shopper's mobile contact numbers on file. Every method is scoped to a single owner: one
/// shopper can never see, use, or delete another's number.
/// </summary>
public interface IContactNumberService
{
    /// <summary>
    /// Register a mobile number for a shopper. The provider validates it; an unusable destination is
    /// rejected here (not at send time) and what gets stored is the provider's canonical E.164 form.
    /// </summary>
    Task<ContactNumberRegistrationResult> RegisterAsync(string ownerId, string rawNumber, CancellationToken ct);

    /// <summary>The caller's registered numbers.</summary>
    Task<IReadOnlyList<ContactNumber>> ListAsync(string ownerId, CancellationToken ct);

    /// <summary>Remove one of the caller's numbers. Returns false if it is not the caller's or does not exist.</summary>
    Task<bool> RemoveAsync(string ownerId, int contactNumberId, CancellationToken ct);
}

public enum ContactNumberRegistrationOutcome
{
    Registered = 0,

    /// <summary>The provider does not consider the number a usable destination.</summary>
    Rejected = 1
}

public record ContactNumberRegistrationResult(ContactNumberRegistrationOutcome Outcome, ContactNumber? ContactNumber, string? RejectReason);
