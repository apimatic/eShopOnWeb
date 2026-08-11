using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the money-movement lifecycle of an order on top of the existing order model and the
/// PayPal gateway. Each action is separately invocable and idempotent in effect.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Place an order for the shopper from catalog items; it starts awaiting payment.</summary>
    Task<Order> PlaceOrderAsync(
        string buyerId, IReadOnlyCollection<OrderLine> lines, ShippingAddressInput? shipTo,
        CancellationToken cancellationToken = default);

    /// <summary>Authorize (hold) the order total for the shopper. Idempotent per order.</summary>
    Task<Order> PayAsync(
        int orderId, string buyerId, PaymentInstruction instruction,
        CancellationToken cancellationToken = default);

    /// <summary>Operator: fulfil the order, capturing the held funds (renewing a stale hold first).</summary>
    Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Operator: cancel before fulfilment, releasing the held funds.</summary>
    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Refund the shopper's captured payment, full or partial, under an idempotency key.</summary>
    Task<PaymentRefund> RefundAsync(
        int orderId, string buyerId, decimal? amount, string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>The caller's orders with their payment state.</summary>
    Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);
}
