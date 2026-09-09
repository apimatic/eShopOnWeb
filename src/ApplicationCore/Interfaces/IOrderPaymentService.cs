using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the money-movement lifecycle of an order: place, authorize (hold),
/// fulfil (capture), cancel (void), and refund. Each action is separately invocable
/// and idempotent in effect.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Place an order from catalog items for the given buyer. Starts awaiting payment.</summary>
    Task<Order> CreateOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines, Address shipToAddress, CancellationToken cancellationToken = default);

    /// <summary>Authorize (hold) the order total using a one-off card or a saved card.</summary>
    Task<Order> PayAsync(int orderId, string buyerId, PaymentInstrument instrument, CancellationToken cancellationToken = default);

    /// <summary>Operator action: fulfil the order, capturing the held funds (renewing a stale hold if needed).</summary>
    Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: cancel the order before fulfilment, releasing any hold.</summary>
    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Refund the captured payment, fully or partially, keyed by a caller idempotency key.</summary>
    Task<OrderRefund> RefundAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Get one of the caller's orders (throws if it is not theirs).</summary>
    Task<Order> GetOrderForBuyerAsync(int orderId, string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Get all of the caller's orders with their payment state.</summary>
    Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);
}

/// <summary>A requested catalog item and quantity for a new order.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

/// <summary>
/// How to pay: either a one-off <see cref="Card"/> or a previously saved card identified
/// by <see cref="SavedPaymentMethodId"/>. Exactly one must be supplied.
/// </summary>
public record PaymentInstrument(PayPalCardDetails? Card, int? SavedPaymentMethodId);
