using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Registers, lists and removes the mobile numbers a shopper puts on file. All
/// operations are scoped to the owning shopper.
/// </summary>
public interface IContactNumberService
{
    /// <summary>
    /// Validates a raw number with the provider and, if it is a usable destination,
    /// stores the provider's canonical E.164 form for the shopper. Rejects an unusable
    /// number here, not at send time.
    /// </summary>
    Task<ContactNumberRegistrationResult> RegisterAsync(string ownerId, string rawNumber, CancellationToken cancellationToken = default);

    /// <summary>The caller's registered numbers.</summary>
    Task<IReadOnlyList<ContactNumber>> ListAsync(string ownerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes one of the caller's numbers and calls off any still-scheduled messages
    /// queued to it, so nothing is sent to it again. Returns false if the number is not
    /// found among the caller's numbers.
    /// </summary>
    Task<bool> RemoveAsync(string ownerId, int contactNumberId, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of registering a contact number.</summary>
public record ContactNumberRegistrationResult(bool Succeeded, ContactNumber? ContactNumber, string? RejectionReason)
{
    public static ContactNumberRegistrationResult Ok(ContactNumber contactNumber) => new(true, contactNumber, null);
    public static ContactNumberRegistrationResult Rejected(string reason) => new(false, null, reason);
}
