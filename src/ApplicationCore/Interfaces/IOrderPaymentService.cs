using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the order + payment lifecycle: place, authorize (pay), fulfil (capture),
/// cancel (void), refund, and shopper order listing. Each action is independently invocable.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Places an order from catalog items for the buyer. The order starts awaiting payment.</summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineInput> lines, ShippingAddressInput? shippingAddress, CancellationToken cancellationToken = default);

    /// <summary>Authorizes (holds) the order total against a card or a saved card. Idempotent in effect.</summary>
    Task<OrderPayment> PayAsync(string buyerId, int orderId, PayInstruction instruction, CancellationToken cancellationToken = default);

    /// <summary>Operator action: fulfils the order and captures the money, renewing a stale hold if needed.</summary>
    Task<OrderPayment> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: cancels before fulfilment, releasing the held funds.</summary>
    Task<OrderPayment> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refunds the captured payment for the buyer's order, fully or partially. Idempotent per key.
    /// Returns the updated payment; the created/returned refund is identified by <paramref name="idempotencyKey"/>.
    /// </summary>
    Task<OrderPayment> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Lists the buyer's orders with their payment state.</summary>
    Task<IReadOnlyList<OrderWithPayment>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);
}
