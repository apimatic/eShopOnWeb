using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Registers and manages a shopper's contact numbers. All operations are scoped to the owning
/// shopper: one shopper can never see, use, or delete another's number.
/// </summary>
public interface IContactNumberService
{
    /// <summary>
    /// Registers a mobile number for the shopper. The provider validates it; a number the provider
    /// does not consider a usable destination is rejected here (<see cref="ResultStatus.Invalid"/>).
    /// The stored value is the provider's canonical form of the number.
    /// </summary>
    Task<Result<ContactNumber>> RegisterAsync(string buyerId, string rawNumber, CancellationToken cancellationToken = default);

    /// <summary>The caller's registered numbers.</summary>
    Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Removes one of the caller's numbers. Afterwards nothing is sent to it again.</summary>
    Task<Result> DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default);
}
