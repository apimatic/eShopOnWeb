using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the pay-for-an-order flow: place, authorize (pay), fulfil (capture), cancel (void),
/// refund, and list the caller's orders. Owns idempotency state and the payment state machine; delegates
/// money movement to <see cref="IPayPalGateway"/>.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Places an order from catalog items for the buyer, awaiting payment. Returns the order id.</summary>
    Task<PlaceOrderResult> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> items,
        Address shipToAddress, CancellationToken ct);

    /// <summary>Authorizes the order total (holds the money) using a one-off card or a saved card.</summary>
    Task<OrderPaymentView> PayAsync(string buyerId, int orderId, CardDetails? card, int? savedCardId,
        CancellationToken ct);

    /// <summary>Operator action: fulfils the order, capturing the money (renewing a stale hold first).</summary>
    Task<OrderPaymentView> FulfilAsync(int orderId, CancellationToken ct);

    /// <summary>Operator action: cancels before fulfilment, releasing the held funds.</summary>
    Task<OrderPaymentView> CancelAsync(int orderId, CancellationToken ct);

    /// <summary>Refunds the captured payment, in full or in part, under a caller-supplied idempotency key.</summary>
    Task<RefundResult> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey,
        CancellationToken ct);

    /// <summary>The caller's orders with their payment state.</summary>
    Task<IReadOnlyList<OrderPaymentView>> GetMyOrdersAsync(string buyerId, CancellationToken ct);
}
