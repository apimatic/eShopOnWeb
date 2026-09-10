using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the pay-for-an-order flow: it owns the eShop <c>Order</c> + <c>Payment</c> domain
/// state and calls <see cref="IPayPalGateway"/> for money movement. Payment operations are
/// idempotent in effect — a double-click never authorizes or captures twice.
/// </summary>
public interface IPaymentService
{
    /// <summary>Places an order from catalog items (prices read from the catalog) awaiting payment. Returns the order id.</summary>
    Task<int> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<OrderLineInput> lines,
        ShippingAddressInput? shipTo,
        CancellationToken ct);

    /// <summary>
    /// Authorizes (holds) the order total, paying with either a one-off <paramref name="card"/> or one of
    /// the shopper's saved cards (<paramref name="savedPaymentMethodId"/>). Exactly one must be supplied.
    /// Shopper-scoped: acts only on the caller's own order, and a saved card must belong to the caller.
    /// </summary>
    Task<OrderPaymentView> AuthorizeAsync(
        int orderId,
        string buyerId,
        CardDetails? card,
        int? savedPaymentMethodId,
        CancellationToken ct);

    /// <summary>Operator action: fulfils the order and captures the money (renewing a stale hold if needed).</summary>
    Task<OrderPaymentView> FulfilAsync(int orderId, CancellationToken ct);

    /// <summary>Operator action: cancels before fulfilment, releasing the held funds.</summary>
    Task<OrderPaymentView> CancelAsync(int orderId, CancellationToken ct);

    /// <summary>Refunds a captured order, in full or in part. Shopper-scoped; idempotent per key.</summary>
    Task<RefundView> RefundAsync(
        int orderId,
        string buyerId,
        decimal? amount,
        string idempotencyKey,
        CancellationToken ct);

    /// <summary>The caller's own orders with their payment state.</summary>
    Task<IReadOnlyList<OrderPaymentView>> GetOrdersForBuyerAsync(string buyerId, CancellationToken ct);
}
