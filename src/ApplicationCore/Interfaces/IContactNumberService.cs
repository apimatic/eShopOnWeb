using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Outcome of a registration attempt: either a stored number, or the reasons it was rejected.</summary>
public record ContactNumberRegistration(bool Success, ContactNumber? ContactNumber, IReadOnlyList<string> Errors);

/// <summary>Manages a shopper's own on-file mobile numbers. Every operation is scoped to one buyer.</summary>
public interface IContactNumberService
{
    /// <summary>
    /// Validates a raw number with the provider and, if it is a usable destination, stores its
    /// canonical form for the buyer. An unusable number is rejected here, not at send time.
    /// </summary>
    Task<ContactNumberRegistration> RegisterAsync(string buyerId, string rawNumber, string? countryCode, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Removes one of the buyer's numbers. Returns false if it is not theirs or does not exist.</summary>
    Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default);
}
