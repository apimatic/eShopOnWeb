using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The shopper's contact numbers on file. Every operation is scoped to a single shopper
/// (<c>buyerId</c>): a shopper never sees, uses or deletes another's number.
/// </summary>
public interface IContactNumberService
{
    /// <summary>
    /// Registers a mobile number for the shopper. The number is validated with the provider and
    /// stored in the provider's canonical form; an unusable number is rejected here (Invalid).
    /// Returns the registered number's id.
    /// </summary>
    Task<Result<int>> RegisterAsync(string buyerId, string rawNumber, CancellationToken cancellationToken = default);

    /// <summary>The shopper's registered numbers.</summary>
    Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Removes one of the shopper's numbers. NotFound if it is not theirs (or does not exist).</summary>
    Task<Result> DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default);
}
