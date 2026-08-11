using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

/// <summary>
/// Orchestrates the "pay for an order" flow: place, authorize (hold), fulfil (capture),
/// cancel (void), and refund. Each step is separately invocable and idempotent in effect.
/// </summary>
public interface IPaymentService
{
    /// <summary>Places an order for the shopper from catalog items. Returns the new order id.</summary>
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> lines,
        ShippingAddressInput? address, CancellationToken cancellationToken = default);

    /// <summary>
    /// Authorizes the order total (holds the money) using either one-off <paramref name="card"/>
    /// details or one of the shopper's saved cards (<paramref name="savedPaymentMethodId"/>).
    /// </summary>
    Task<Order> PayOrderAsync(string buyerId, int orderId, PayPalCardDetails? card,
        int? savedPaymentMethodId, CancellationToken cancellationToken = default);

    /// <summary>Fulfils the order and captures the money (operator action). Renews a stale hold if needed.</summary>
    Task<Order> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Cancels the order before fulfilment, releasing the held funds (operator action).</summary>
    Task<Order> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Refunds a captured payment, fully or in part. Returns the order and the refund id.</summary>
    Task<(Order Order, string RefundId)> RefundOrderAsync(string buyerId, int orderId, decimal? amount,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>The caller's own orders, each with its payment state.</summary>
    Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);
}
