using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>One line of a new order: a catalog item and how many of it.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

/// <summary>How to pay: exactly one of a raw card (one-off) or a saved card id (reuse a vaulted card).</summary>
public record PayInstruction(CardDetails? Card, int? SavedCardId);

/// <summary>An order paired with its payment state, for the caller's order list.</summary>
public record MyOrderView(Order Order, OrderPayment? Payment);

/// <summary>
/// Orchestrates the money movement for orders: place, authorize (hold), fulfil (capture), cancel (release),
/// refund, and read back the caller's orders with their payment state. Coordinates the domain aggregates with
/// the PayPal gateway and enforces idempotency and per-shopper ownership.
/// </summary>
public interface IPaymentService
{
    /// <summary>Places an order from catalog items for the given shopper. The order starts awaiting payment.</summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, Address shipToAddress, CancellationToken cancellationToken = default);

    /// <summary>Authorizes (holds) the order total. Idempotent: a repeated call returns the existing authorization.</summary>
    Task<OrderPayment> AuthorizeAsync(int orderId, string buyerId, PayInstruction instruction, CancellationToken cancellationToken = default);

    /// <summary>Operator action: fulfil the order and capture the held funds, renewing a stale hold if needed. Idempotent.</summary>
    Task<OrderPayment> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: cancel before fulfilment, releasing the held funds. Idempotent.</summary>
    Task<OrderPayment> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Refunds a captured payment (full or partial). Idempotent per caller-supplied key; guards against over-refund.</summary>
    Task<PaymentRefund> RefundAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Returns the caller's orders together with their payment state.</summary>
    Task<IReadOnlyList<MyOrderView>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Loads a single payment for the caller's order (ownership enforced), or throws if not found/owned.</summary>
    Task<OrderPayment> GetOwnedPaymentAsync(int orderId, string buyerId, CancellationToken cancellationToken = default);
}
