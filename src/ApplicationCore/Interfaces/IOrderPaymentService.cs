using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the pay-for-an-order flow: place, authorize (pay), fulfil (capture), cancel (void)
/// and refund. Enforces order ownership for shopper-scoped actions and drives PayPal via
/// <see cref="IPayPalClient"/>.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Place an order for the buyer from catalog items; starts awaiting payment.</summary>
    Task<int> PlaceOrderAsync(string buyerId, IEnumerable<OrderLineRequest> lines, Address shipToAddress,
        CancellationToken cancellationToken = default);

    /// <summary>Authorize (hold) the order total. Idempotent: a double-click never authorizes twice.</summary>
    Task<Order> PayOrderAsync(string buyerId, int orderId, PayOrderCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Operator action: capture the held funds and mark fulfilled, renewing a stale hold.</summary>
    Task<Order> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: release held funds and mark cancelled (only before fulfilment).</summary>
    Task<Order> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Refund a captured order, in full or in part. Idempotent on the caller's key.</summary>
    Task<Refund> RefundOrderAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>The buyer's own orders with their payment state.</summary>
    Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);
}
