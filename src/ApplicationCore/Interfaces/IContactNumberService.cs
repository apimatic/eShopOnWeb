using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages a shopper's on-file contact numbers. Every operation is scoped to a single owner, so
/// one shopper can never see, use, or delete another's number.
/// </summary>
public interface IContactNumberService
{
    /// <summary>
    /// Registers a number for a shopper. The provider is asked whether the number is a usable
    /// destination; if not, the registration is rejected here. What gets stored is the provider's
    /// own canonical form of the number.
    /// </summary>
    Task<ContactNumberRegistration> RegisterAsync(string ownerId, string rawNumber, CancellationToken cancellationToken = default);

    /// <summary>The numbers a shopper has on file.</summary>
    Task<IReadOnlyList<ContactNumber>> ListAsync(string ownerId, CancellationToken cancellationToken = default);

    /// <summary>Removes one of the shopper's numbers. Returns false if it does not exist for this owner.</summary>
    Task<bool> DeleteAsync(string ownerId, int contactNumberId, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of a registration. On success <see cref="ContactNumber"/> is set; on rejection <see cref="Error"/> explains why.</summary>
public record ContactNumberRegistration(bool Success, ContactNumber? ContactNumber, string? Error);
