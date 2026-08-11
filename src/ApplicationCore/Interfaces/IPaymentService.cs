using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the money movement for an order: placing it, authorizing (holding), fulfilling
/// (capturing), cancelling (voiding) and refunding. Each method drives PayPal and then records the
/// resulting state on the order. All operations are idempotent in effect.
/// </summary>
public interface IPaymentService
{
    /// <summary>Create an order for the shopper from catalog items, priced from the catalog, awaiting payment.</summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, Address shipToAddress, CancellationToken cancellationToken = default);

    /// <summary>Authorize the order total (place a hold). No money is taken. Idempotent per order.</summary>
    Task AuthorizeOrderAsync(Order order, PaymentInstruction instruction, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fulfil the order: capture the held funds. Renews a stale authorization first when possible.
    /// Idempotent per order (a second call returns without capturing again).
    /// </summary>
    Task FulfilOrderAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>Cancel before fulfilment: void the hold so no money moves. Idempotent per order.</summary>
    Task CancelOrderAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refund the capture, fully or partially, guarded so total refunds never exceed the captured
    /// amount. Repeating the same idempotency key returns the original refund instead of refunding twice.
    /// </summary>
    Task<PaymentRefund> RefundOrderAsync(Order order, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default);
}
