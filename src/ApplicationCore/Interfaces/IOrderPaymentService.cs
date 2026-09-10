using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates an order's money movements against PayPal while keeping the domain the source of truth:
/// place, authorize (pay), fulfil (capture), cancel (void) and refund.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Creates an order for the shopper from catalog items, awaiting payment. No money moves.</summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> lines, Address shipToAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Authorizes (holds) the order total, paying with a one-off <paramref name="card"/> or a saved card
    /// (<paramref name="savedPaymentMethodId"/>). Idempotent: a repeat while already authorized is a no-op.
    /// </summary>
    Task<Order> PayOrderAsync(string buyerId, int orderId, CardDetails? card, int? savedPaymentMethodId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: captures the held funds, renewing a stale authorization first if needed.</summary>
    Task<Order> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: releases the held funds before fulfilment so no money moves.</summary>
    Task<Order> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refunds the shopper's captured order, in full (<paramref name="amount"/> null) or in part. The
    /// <paramref name="idempotencyKey"/> makes a repeat under the same key return the original refund.
    /// </summary>
    Task<RefundOutcome> RefundOrderAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default);
}
