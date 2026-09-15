using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages a shopper's registered mobile contact numbers. Every operation is scoped to a single shopper
/// (<c>buyerId</c>): one shopper can never see, use, or delete another's number.
/// </summary>
public interface IContactNumberService
{
    /// <summary>
    /// Registers a number for the shopper after validating it with the provider and storing the provider's
    /// canonical E.164 form. A number the provider does not consider usable is rejected here.
    /// </summary>
    Task<ContactNumberRegistrationResult> RegisterAsync(string buyerId, string rawPhoneNumber, CancellationToken cancellationToken);

    /// <summary>Returns the caller's registered numbers.</summary>
    Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken);

    /// <summary>Removes one of the caller's numbers. Returns false when it does not exist or is not the caller's.</summary>
    Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken);
}

/// <summary>Outcome of a contact-number registration attempt.</summary>
public record ContactNumberRegistrationResult(bool Success, int ContactNumberId, string? CanonicalNumber, string? Error);
