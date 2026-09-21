using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Manages a shopper's on-file mobile contact numbers (Flow 1).</summary>
public interface IContactNumberService
{
    /// <summary>
    /// Registers a mobile number for the shopper after the provider confirms it is a usable
    /// destination, storing the provider's canonical E.164 form. When the number is not usable the
    /// result's <see cref="ContactNumberRegistration.Registered"/> is false.
    /// </summary>
    Task<ContactNumberRegistration> RegisterAsync(string buyerId, string phoneNumber, CancellationToken ct);

    /// <summary>The caller's registered numbers.</summary>
    Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken ct);

    /// <summary>Removes one of the caller's numbers. Returns false if it is not theirs / does not exist.</summary>
    Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken ct);
}

/// <summary>Outcome of a contact-number registration.</summary>
public record ContactNumberRegistration(
    bool Registered, int? ContactNumberId, string? CanonicalNumber, string? Reason);
