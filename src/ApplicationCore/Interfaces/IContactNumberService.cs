using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Outcome of registering a contact number.</summary>
public record ContactNumberRegistration(bool Succeeded, ContactNumber? ContactNumber, IReadOnlyList<string> Errors);

/// <summary>
/// Manages a shopper's on-file mobile numbers. Every operation is scoped to a single
/// shopper: one shopper can never see, use, or delete another's number.
/// </summary>
public interface IContactNumberService
{
    /// <summary>
    /// Validates and registers a number for the shopper. A number the provider does not
    /// consider a usable destination is rejected here. The provider's canonical form is stored.
    /// </summary>
    Task<ContactNumberRegistration> RegisterAsync(string buyerId, string rawNumber, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Removes one of the shopper's numbers. Returns false if it is not theirs or not found.</summary>
    Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default);
}
