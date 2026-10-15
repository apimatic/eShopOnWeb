using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Specification;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Store for claims that must be refused by the database itself (primary key / concurrency token),
/// not by a read-then-write check that lets two concurrent callers through.
/// </summary>
public interface IClaimStore<T> where T : class, IAggregateRoot
{
    /// <returns>true when the claim was inserted; false when a claim with the same key already exists.</returns>
    Task<bool> TryAddAsync(T claim, CancellationToken cancellationToken);

    /// <summary>Reads the claim's current state from the store (never a stale tracked copy).</summary>
    Task<T?> FindAsync(object[] key, CancellationToken cancellationToken);

    Task<IReadOnlyList<T>> ListAsync(ISpecification<T> specification, CancellationToken cancellationToken);

    /// <returns>true when saved; false when another writer changed the claim since it was read.</returns>
    Task<bool> TryUpdateAsync(T claim, CancellationToken cancellationToken);

    Task RemoveAsync(T claim, CancellationToken cancellationToken);
}
