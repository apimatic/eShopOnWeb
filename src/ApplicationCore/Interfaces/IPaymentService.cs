using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A requested order line: a catalog item and how many of it.</summary>
public record OrderLineInput(int CatalogItemId, int Units);

/// <summary>
/// Orchestrates the money movement for an order: place, authorize (hold), fulfil (capture),
/// cancel (release) and refund. Every operation is idempotent in effect and scoped to its caller.
/// </summary>
public interface IPaymentService
{
    /// <summary>Place an order for the shopper from catalog items, awaiting payment.</summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineInput> lines,
        Address shipToAddress, CancellationToken ct = default);

    /// <summary>
    /// Authorize (hold) the order total using either one-off card details or one of the shopper's
    /// saved cards. Idempotent: a repeat never authorizes the shopper twice.
    /// </summary>
    Task<Order> AuthorizeAsync(int orderId, string buyerId, CardDetails? card, int? paymentMethodId,
        CancellationToken ct = default);

    /// <summary>
    /// Fulfil the order — capturing (taking) the held funds. Renews a stale authorization rather than
    /// failing outright; if it can no longer be renewed, reports so in operator-actionable terms.
    /// Idempotent: a repeat never captures twice.
    /// </summary>
    Task<Order> FulfilAsync(int orderId, CancellationToken ct = default);

    /// <summary>Cancel before fulfilment, releasing any held funds. Idempotent.</summary>
    Task<Order> CancelAsync(int orderId, CancellationToken ct = default);

    /// <summary>
    /// Refund a captured payment, fully or partially, under a caller-supplied idempotency key.
    /// A repeat under the same key does not refund twice; a partial refund can never take the total
    /// refunded beyond what was captured.
    /// </summary>
    Task<(Order Order, Refund Refund)> RefundAsync(int orderId, string buyerId, decimal? amount,
        string idempotencyKey, CancellationToken ct = default);
}
