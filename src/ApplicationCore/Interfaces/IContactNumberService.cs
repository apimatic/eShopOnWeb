using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// A shopper's own mobile contact numbers. Every operation is scoped to the caller: no shopper
/// can see, use or delete another's number.
/// </summary>
public interface IContactNumberService
{
    /// <summary>
    /// Registers a mobile number for a shopper. The number is validated and canonicalised by the
    /// provider first; an unusable destination is rejected here, and the stored value is the
    /// provider's canonical E.164 form, not the raw input.
    /// </summary>
    Task<ContactNumberRegistrationResult> RegisterAsync(string ownerId, string rawPhoneNumber, string? defaultCountryCode, CancellationToken cancellationToken = default);

    /// <summary>The caller's registered numbers.</summary>
    Task<IReadOnlyList<ContactNumber>> ListAsync(string ownerId, CancellationToken cancellationToken = default);

    /// <summary>Removes one of the caller's numbers. Returns false if it isn't theirs / doesn't exist.</summary>
    Task<bool> DeleteAsync(string ownerId, int contactNumberId, CancellationToken cancellationToken = default);
}
