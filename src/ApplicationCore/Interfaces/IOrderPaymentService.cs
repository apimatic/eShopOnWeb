using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the pay-for-an-order flow on top of the existing order model and PayPal:
/// place → authorize (hold) → fulfil (capture) → cancel (void) / refund.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Place an order from catalog items for a buyer. The order awaits payment.</summary>
    Task<Order> PlaceOrderAsync(
        string buyerId, IReadOnlyCollection<OrderLine> lines, Address shipToAddress,
        CancellationToken cancellationToken = default);

    /// <summary>Authorize the order total (place a hold). Idempotent: a repeat is a no-op.</summary>
    Task<Order> AuthorizePaymentAsync(
        string buyerId, int orderId, PaymentInstrument instrument,
        CancellationToken cancellationToken = default);

    /// <summary>Operator action: fulfil the order, capturing the held funds.</summary>
    Task<Order> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: cancel before fulfilment, releasing the held funds.</summary>
    Task<Order> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Refund a captured order, fully (null amount) or partially. Idempotent per key.</summary>
    Task<PaymentRefund> RefundOrderAsync(
        string buyerId, int orderId, decimal? amount, string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>The caller's orders with their payment state.</summary>
    Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(
        string buyerId, CancellationToken cancellationToken = default);
}
