using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Messaging;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Manages a shopper's on-file contact numbers. Every operation is scoped to one shopper.</summary>
public interface IContactNumberService
{
    /// <summary>
    /// Validates a number with the provider and, if it is a usable destination, stores its canonical form.
    /// Rejects unusable numbers up front rather than at send time.
    /// </summary>
    Task<ContactNumberRegistrationResult> RegisterAsync(string buyerId, string rawNumber, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Removes one of the shopper's numbers. Returns false if it does not belong to the shopper / does not exist.</summary>
    Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default);
}
