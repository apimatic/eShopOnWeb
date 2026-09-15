using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages a shopper's mobile contact numbers. All operations are scoped to one shopper
/// (<c>ownerId</c>): a shopper can only see, register against, and delete their own numbers.
/// </summary>
public interface IContactNumberService
{
    /// <summary>
    /// Registers a mobile number for the shopper. The provider validates it and returns its canonical
    /// E.164 form, which is what gets stored; a number the provider does not consider a usable
    /// destination is rejected here rather than at send time.
    /// </summary>
    Task<ContactNumberRegistrationResult> RegisterAsync(string ownerId, string rawNumber, CancellationToken cancellationToken);

    /// <summary>The shopper's registered numbers.</summary>
    Task<IReadOnlyList<ContactNumber>> ListAsync(string ownerId, CancellationToken cancellationToken);

    /// <summary>
    /// Removes one of the shopper's numbers. Returns false when no such number belongs to the shopper.
    /// Afterwards the number no longer appears among the shopper's numbers and nothing is sent to it again.
    /// </summary>
    Task<bool> DeleteAsync(string ownerId, int contactNumberId, CancellationToken cancellationToken);
}

/// <summary>
/// The outcome of a registration: either rejected (the provider does not consider the number a usable
/// destination) or accepted with the stored canonical number.
/// </summary>
public record ContactNumberRegistrationResult(bool Rejected, ContactNumber? ContactNumber);
