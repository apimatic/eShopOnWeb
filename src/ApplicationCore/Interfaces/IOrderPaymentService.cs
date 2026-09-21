using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A requested order line: a catalog item id and a quantity.</summary>
public record OrderLineInput(int CatalogItemId, int Quantity);

/// <summary>Optional shipping address for a placed order.</summary>
public record ShippingAddressInput(string Street, string City, string State, string Country, string ZipCode);

/// <summary>The outcome of a refund: the affected order and the refund that was recorded.</summary>
public record RefundOutcome(Order Order, OrderRefund Refund);

/// <summary>One PayPal transaction reconciled against an eShop order it matched.</summary>
public record MatchedTransaction(
    int OrderId,
    string? PayPalTransactionId,
    string? PayPalStatus,
    decimal? PayPalAmount,
    string? Currency,
    decimal EShopCapturedAmount,
    OrderStatus EShopStatus);

/// <summary>An eShop order that was captured but did not appear in PayPal's transaction records for the range.</summary>
public record UnmatchedOrder(int OrderId, decimal CapturedAmount, string Currency, OrderStatus Status, string? CaptureId);

/// <summary>
/// A reconciliation report for a date range: PayPal's own transaction records lined up against eShop
/// orders, so a payment PayPal knows about that eShop doesn't (or the reverse) is visible.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    int EShopCapturedOrderCount,
    bool RangeEmpty,
    IReadOnlyList<MatchedTransaction> Matched,
    IReadOnlyList<PayPalTransaction> InPayPalNotInEShop,
    IReadOnlyList<UnmatchedOrder> InEShopNotInPayPal);

/// <summary>
/// Orchestrates the order money lifecycle over the domain and the PayPal gateway. Each action is
/// separately invocable; operations are idempotent in effect so a double-click never charges twice.
/// Shopper-scoped methods act only on the caller's own orders; operator methods act on any order.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Places an order from catalog items for the buyer. Starts <see cref="OrderStatus.AwaitingPayment"/>.</summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> lines, ShippingAddressInput? shipTo, CancellationToken ct);

    /// <summary>
    /// Authorizes (holds) the order total for the buyer's own order, funded by a one-off
    /// <paramref name="card"/> or a saved card (<paramref name="savedPaymentMethodId"/>). Idempotent:
    /// re-paying an already-authorized order returns it unchanged.
    /// </summary>
    Task<Order> PayAsync(string buyerId, int orderId, CardDetails? card, int? savedPaymentMethodId, CancellationToken ct);

    /// <summary>Operator: captures the held funds (takes the money). Renews a stale hold rather than failing.</summary>
    Task<Order> FulfilAsync(int orderId, CancellationToken ct);

    /// <summary>Operator: cancels before fulfilment by releasing the held funds. No money moves.</summary>
    Task<Order> CancelAsync(int orderId, CancellationToken ct);

    /// <summary>
    /// Refunds the buyer's own captured order, fully (<paramref name="amount"/> null) or partially, never
    /// beyond the captured amount. The idempotency key makes a repeat return the same refund.
    /// </summary>
    Task<RefundOutcome> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken ct);

    /// <summary>The buyer's own orders with payment state, newest first.</summary>
    Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken ct);

    /// <summary>Operator: reconciles PayPal's transaction records for a range against eShop orders (all pages).</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
