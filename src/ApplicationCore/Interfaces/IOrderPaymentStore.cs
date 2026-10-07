using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Loads and saves an order together with its payment attempts and refunds.
/// </summary>
public interface IOrderPaymentStore
{
    /// <summary>
    /// Loads the latest state of the order, applies <paramref name="change"/> and saves it. When another request
    /// saved the order in between (the order's payment version changed, or a claimed key already exists), the
    /// change is re-applied to the fresh state — so every decision <paramref name="change"/> takes is taken
    /// against what is actually stored. Returns null when the order does not exist.
    /// </summary>
    Task<TResult?> UpdateAsync<TResult>(int orderId, Func<Order, TResult> change, CancellationToken cancellationToken = default)
        where TResult : class;

    /// <summary>Reads an order with its payment attempts, refunds and (optionally) its payment record. Not tracked.</summary>
    Task<Order?> GetAsync(int orderId, bool includePaymentRecord, CancellationToken cancellationToken = default);

    /// <summary>Reads a buyer's orders with items, payment attempts and refunds, newest first. Not tracked.</summary>
    Task<IReadOnlyList<Order>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);
}
