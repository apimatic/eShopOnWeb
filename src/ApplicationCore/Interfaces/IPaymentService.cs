using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the order-payment lifecycle: place → authorize (hold) → fulfil (capture) or cancel
/// (void) → refund, tying the existing Order aggregate to its PayPal-owned Payment.
/// </summary>
public interface IPaymentService
{
    /// <summary>Places an order from catalog items for the shopper and starts it awaiting payment.</summary>
    Task<PlacedOrder> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> lines, Address shipToAddress,
        CancellationToken ct = default);

    /// <summary>Authorizes (holds) the order total using a one-off card or a saved card. Idempotent.</summary>
    Task<PaymentView> AuthorizeAsync(int orderId, string buyerId, PayInstruction instruction,
        CancellationToken ct = default);

    /// <summary>Operator action: fulfils the order and captures the money. Renews a stale hold if needed.</summary>
    Task<PaymentView> FulfilAsync(int orderId, CancellationToken ct = default);

    /// <summary>Operator action: cancels before fulfilment, releasing any held funds. Idempotent.</summary>
    Task<PaymentView> CancelAsync(int orderId, CancellationToken ct = default);

    /// <summary>Refunds a captured order in full or in part; de-duplicated on the idempotency key.</summary>
    Task<(int RefundId, PaymentView Payment)> RefundAsync(int orderId, string callerBuyerId, bool callerIsAdmin,
        decimal? amount, string idempotencyKey, string? noteToPayer, CancellationToken ct = default);

    /// <summary>Returns the caller's own orders with their payment state.</summary>
    Task<IReadOnlyList<OrderPaymentView>> GetMyOrdersAsync(string buyerId, CancellationToken ct = default);
}
