using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Places orders from catalog items and drives their PayPal payment lifecycle (pay, refund, query).
/// Reuses the existing <see cref="Order"/> / <see cref="OrderItem"/> aggregate; a payment adds state
/// to that order rather than introducing a parallel model.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>
    /// Places an order for <paramref name="buyerId"/> from the supplied catalog item quantities.
    /// The order starts in <see cref="PaymentStatus.AwaitingPayment"/>.
    /// </summary>
    Task<PlaceOrderResult> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineInput> lines, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pays for the buyer's order with PayPal, either with one-off card details or a saved card.
    /// Idempotent: paying an already-paid order returns the existing payment without re-charging.
    /// </summary>
    Task<PayOrderResult> PayOrderAsync(string buyerId, int orderId, OrderPaymentInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fully refunds the buyer's paid order. Idempotent: refunding an already-refunded order
    /// returns the existing refund without issuing another.
    /// </summary>
    Task<RefundOrderResult> RefundOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken = default);

    /// <summary>Returns the buyer's orders with their payment state.</summary>
    Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);
}

/// <summary>A requested order line: a catalog item and the quantity to purchase.</summary>
public sealed record OrderLineInput(int CatalogItemId, int Quantity);

/// <summary>
/// How to pay an order: exactly one of <see cref="Card"/> (one-off) or <see cref="SavedPaymentMethodId"/>
/// (a saved card belonging to the caller) must be supplied.
/// </summary>
public sealed record OrderPaymentInput
{
    public CardDetails? Card { get; init; }
    public int? SavedPaymentMethodId { get; init; }
}

public enum PlaceOrderOutcome
{
    Placed,
    EmptyOrder,
    CatalogItemNotFound
}

public sealed record PlaceOrderResult(PlaceOrderOutcome Outcome, Order? Order = null, string? Error = null);

public enum PayOrderOutcome
{
    Paid,
    AlreadyPaid,
    OrderNotFound,
    AlreadyRefunded,
    InvalidRequest,
    SavedCardNotFound,
    PaymentFailed
}

public sealed record PayOrderResult(PayOrderOutcome Outcome, Order? Order = null, string? Error = null);

public enum RefundOrderOutcome
{
    Refunded,
    AlreadyRefunded,
    OrderNotFound,
    NotPaid,
    RefundFailed
}

public sealed record RefundOrderResult(RefundOrderOutcome Outcome, Order? Order = null, string? Error = null);
