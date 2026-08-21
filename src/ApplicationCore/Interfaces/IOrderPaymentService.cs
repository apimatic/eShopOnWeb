using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A line on a new order: a catalog item and how many of it.</summary>
public record OrderLineCommand(int CatalogItemId, int Quantity);

/// <summary>An optional shipping address for a new order.</summary>
public record AddressCommand(string Street, string City, string State, string Country, string ZipCode);

/// <summary>The request to place a new order from catalog items.</summary>
public record PlaceOrderCommand(IReadOnlyList<OrderLineCommand> Items, AddressCommand? ShipToAddress);

/// <summary>
/// How to pay: either raw <see cref="Card"/> details for a one-off payment, or a saved card by its
/// <see cref="SavedPaymentMethodId"/>. Exactly one must be supplied.
/// </summary>
public record PaymentInstrument
{
    public PayPalCardDetails? Card { get; init; }
    public int? SavedPaymentMethodId { get; init; }
}

/// <summary>An order paired with its payment state (for GET /api/my-orders).</summary>
public record MyOrderResult(Order Order, OrderPayment? Payment);

/// <summary>The outcome of a refund request.</summary>
public record RefundResult(int RefundId, string PayPalRefundId, string? Status, decimal Amount, string CurrencyCode, OrderPayment Payment);

/// <summary>
/// Orchestrates the "pay for an order" flow end to end: placing the order, holding the money,
/// taking it at fulfilment, releasing it on cancel, and returning it on refund. Each method acts
/// only within the bounds the caller is allowed (ownership is enforced by the caller passing the
/// buyer id where the action is shopper-scoped).
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Places an order from catalog items and creates its payment in "awaiting payment" state. Returns the order id.</summary>
    Task<int> PlaceOrderAsync(PlaceOrderCommand command, string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Authorizes (holds) the order total. Idempotent: a repeat returns the existing hold. Shopper-scoped.</summary>
    Task<OrderPayment> PayAsync(int orderId, PaymentInstrument instrument, string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Fulfils the order, capturing the held money (renewing a stale hold if needed). Operator action.</summary>
    Task<OrderPayment> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Cancels the order before fulfilment, releasing the held money. Operator action.</summary>
    Task<OrderPayment> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Refunds a captured payment, in full or in part, under a caller-supplied idempotency key. Shopper-scoped.</summary>
    Task<RefundResult> RefundAsync(int orderId, decimal? amount, string idempotencyKey, string buyerId, CancellationToken cancellationToken = default);

    /// <summary>The caller's own orders paired with their payment state.</summary>
    Task<IReadOnlyList<MyOrderResult>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);
}
