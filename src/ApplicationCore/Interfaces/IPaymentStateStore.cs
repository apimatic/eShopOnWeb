using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The store-enforced guards the payment flow relies on to stop duplicate charges and over-refunds.
/// </summary>
public interface IPaymentStateStore
{
    /// <summary>
    /// Inserts <paramref name="claim"/>. Returns false when a row with the same key already exists — the store,
    /// not a prior read, refuses the second claimant.
    /// </summary>
    Task<bool> TryClaimAsync<T>(T claim, CancellationToken cancellationToken) where T : class, IAggregateRoot;

    /// <summary>
    /// Saves a change to an order's payment state under its concurrency stamp. Returns false — after refreshing
    /// <paramref name="order"/> from the store — when another request changed it first.
    /// </summary>
    Task<bool> TrySaveOrderAsync(Order order, CancellationToken cancellationToken);
}
