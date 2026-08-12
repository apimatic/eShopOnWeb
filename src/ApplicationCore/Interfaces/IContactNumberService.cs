using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Flow 1 — a shopper's contact numbers. Every method is scoped to the owning shopper.
/// </summary>
public interface IContactNumberService
{
    /// <summary>
    /// Registers a number for a shopper. The number is validated with the provider up front; one it
    /// does not consider a usable destination is rejected here (an <see cref="ResultStatus.Invalid"/>
    /// result) rather than at send time. What is stored is the provider's canonical form.
    /// </summary>
    Task<Result<ContactNumber>> RegisterAsync(string buyerId, string rawNumber, CancellationToken cancellationToken = default);

    /// <summary>The caller's registered numbers.</summary>
    Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Removes one of the caller's numbers. Returns false if it does not exist or is not theirs.</summary>
    Task<bool> RemoveAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default);
}
