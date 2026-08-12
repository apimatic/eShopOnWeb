using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Registers and removes a shopper's mobile contact numbers. Registration rejects a number the
/// provider does not consider a usable destination and stores the provider's canonical form.
/// </summary>
public interface IContactNumberService
{
    /// <summary>
    /// Validate <paramref name="rawPhoneNumber"/> with the provider and, if usable, store its
    /// canonical form for the shopper. A number already on file for the shopper is returned as-is.
    /// </summary>
    Task<ContactNumberRegistrationResult> RegisterAsync(string buyerId, string rawPhoneNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove one of the shopper's numbers. Returns false if it does not exist or belongs to another
    /// shopper. Any not-yet-sent scheduled message addressed to that number is called off so nothing
    /// is ever sent to it again.
    /// </summary>
    Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of registering a contact number.</summary>
public record ContactNumberRegistrationResult(bool Success, ContactNumber? ContactNumber, IReadOnlyList<string> Errors);
