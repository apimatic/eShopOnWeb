using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>One line of a placed order: a catalog item and how many of it.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

/// <summary>
/// Orchestrates the pay-for-an-order flow across the order aggregate and PayPal: place, authorize
/// (hold), fulfil (capture), cancel (void) and refund. Enforces shopper ownership where required and
/// keeps every payment operation idempotent in effect.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Places an order for <paramref name="buyerId"/> from catalog lines. Starts awaiting payment.</summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines,
        Address? shipToAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Authorizes (holds) the order total, paying with a one-off <paramref name="card"/> or one of the
    /// shopper's saved cards (<paramref name="paymentMethodId"/>). Idempotent: a repeat returns the
    /// existing hold rather than authorizing again. Scoped to <paramref name="buyerId"/>.
    /// </summary>
    Task<Order> PayAsync(string buyerId, int orderId, CardDetails? card, int? paymentMethodId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator action: fulfils the order and captures the money. Renews a stale authorization first
    /// rather than failing outright; if it can no longer be renewed, throws
    /// <see cref="AuthorizationNotRenewableException"/>.
    /// </summary>
    Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: cancels before fulfilment, releasing any held funds.</summary>
    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refunds a fulfilled order's captured payment, in full or in part, under a caller-supplied
    /// idempotency key. Scoped to <paramref name="buyerId"/>. Never refunds beyond what was captured,
    /// and a repeat under the same key returns the original refund.
    /// </summary>
    Task<Refund> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey,
        CancellationToken cancellationToken = default);
}
