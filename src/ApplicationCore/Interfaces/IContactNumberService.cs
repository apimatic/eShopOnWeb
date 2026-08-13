using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages the mobile numbers a shopper puts on file. Every operation is scoped to the calling
/// shopper - one shopper never sees, uses or deletes another's number.
/// </summary>
public interface IContactNumberService
{
    /// <summary>
    /// Registers a number for the shopper. A number the provider does not consider a usable destination
    /// is rejected here (not when a later message fails), and what gets stored is the provider's own
    /// canonical form. Returns the contactNumberId.
    /// </summary>
    Task<int> RegisterAsync(string buyerId, string rawNumber, CancellationToken cancellationToken = default);

    /// <summary>The caller's registered numbers.</summary>
    Task<IReadOnlyList<ContactNumberView>> ListAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Removes one of the caller's numbers. Afterwards it no longer appears and nothing is sent to it again.</summary>
    Task DeleteAsync(int contactNumberId, string buyerId, CancellationToken cancellationToken = default);
}

/// <summary>A registered contact number as shown to its owner.</summary>
public record ContactNumberView(int ContactNumberId, string PhoneNumber, DateTimeOffset RegisteredDate);
