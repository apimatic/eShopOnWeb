using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Shopper-scoped management of the mobile numbers a shopper has on file. Every operation acts only
/// on the given shopper's own numbers.
/// </summary>
public interface IContactNumberService
{
    /// <summary>
    /// Register a mobile number for a shopper. The number is validated with the provider first — an
    /// unusable destination is rejected here (returns <see cref="ResultStatus.Invalid"/>) rather than
    /// at send time — and the provider's canonical E.164 form is what gets stored.
    /// </summary>
    Task<Result<ContactNumber>> RegisterAsync(string buyerId, string rawNumber, CancellationToken cancellationToken = default);

    /// <summary>The shopper's own registered numbers.</summary>
    Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove one of the shopper's numbers. Returns <see cref="ResultStatus.NotFound"/> if it does not
    /// exist or is not theirs. Afterwards it no longer appears among their numbers and nothing is sent to it again.
    /// </summary>
    Task<Result> DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default);
}
