using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

/// <summary>
/// Orchestrates the order-payment lifecycle over the order/payment aggregates and the PayPal gateway.
/// Shopper-scoped methods take the caller's <c>buyerId</c> and act only on that shopper's orders;
/// operator methods (fulfil, cancel) act on any order and are gated by role at the endpoint.
/// </summary>
public interface IPaymentService
{
    /// <summary>Place an order awaiting payment from catalog items. Returns the new order id.</summary>
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> lines,
        ShippingAddressInput? shippingAddress, CancellationToken ct = default);

    /// <summary>Authorize (hold) an order's total. Idempotent: a repeat never authorizes twice.</summary>
    Task<OrderPaymentView> AuthorizeAsync(string buyerId, int orderId, AuthorizeInstruction instruction,
        CancellationToken ct = default);

    /// <summary>Operator: fulfil the order, capturing the held funds. Renews a stale authorization first.</summary>
    Task<OrderPaymentView> FulfilAsync(int orderId, CancellationToken ct = default);

    /// <summary>Operator: cancel before fulfilment, releasing the hold.</summary>
    Task<OrderPaymentView> CancelAsync(int orderId, CancellationToken ct = default);

    /// <summary>Refund a captured payment, full or partial, under a caller-supplied idempotency key.</summary>
    Task<RefundOutcome> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey,
        CancellationToken ct = default);

    /// <summary>The caller's own orders with their payment state.</summary>
    Task<IReadOnlyList<OrderPaymentView>> GetMyOrdersAsync(string buyerId, CancellationToken ct = default);

    /// <summary>A single one of the caller's own orders with its payment state.</summary>
    Task<OrderPaymentView> GetOrderAsync(string buyerId, int orderId, CancellationToken ct = default);
}

/// <summary>Result of a refund: the new refund's local id + PayPal id, plus the refreshed order view.</summary>
public sealed record RefundOutcome(int RefundId, string PayPalRefundId, OrderPaymentView Order);
