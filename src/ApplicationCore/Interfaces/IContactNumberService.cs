using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Outcome of trying to register a mobile number for a shopper.</summary>
public record ContactNumberRegistrationResult(bool Success, ContactNumber? ContactNumber, string? Error)
{
    public static ContactNumberRegistrationResult Ok(ContactNumber number) => new(true, number, null);
    public static ContactNumberRegistrationResult Rejected(string error) => new(false, null, error);
}

/// <summary>
/// Manages the mobile numbers a shopper has on file. Every operation is scoped to the calling
/// shopper: one shopper can never see, use, or delete another's number.
/// </summary>
public interface IContactNumberService
{
    /// <summary>
    /// Registers a mobile number for the shopper. The number is validated with the provider and
    /// stored in the provider's canonical E.164 form; an unusable number is rejected.
    /// </summary>
    Task<ContactNumberRegistrationResult> RegisterAsync(string buyerId, string rawNumber, CancellationToken cancellationToken = default);

    /// <summary>The shopper's registered numbers.</summary>
    Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes one of the shopper's numbers. Afterwards it no longer appears among their numbers
    /// and nothing further will be sent to it (any not-yet-sent scheduled messages to it are
    /// called off). Returns false if the number does not exist for this shopper.
    /// </summary>
    Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default);
}
