using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the pay-for-an-order flow: place, authorize (hold), fulfil (capture), cancel (void),
/// refund, plus the shopper's order list and the operator reconciliation report. Each action is
/// separately invocable and idempotent in effect.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Places an order from catalog items for the shopper and returns the new order id. Starts awaiting payment.</summary>
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines, ShippingAddressInput? address,
        CancellationToken cancellationToken);

    /// <summary>Authorizes (holds) the order total. Repeats are idempotent — never a second hold.</summary>
    Task<PaymentView> AuthorizeAsync(string buyerId, int orderId, PayInstruction instruction,
        CancellationToken cancellationToken);

    /// <summary>Operator marks the order fulfilled; this is when the money is captured. Renews a stale hold first.</summary>
    Task<PaymentView> FulfilAsync(int orderId, CancellationToken cancellationToken);

    /// <summary>Operator cancels before fulfilment; the held funds are released.</summary>
    Task<PaymentView> CancelAsync(int orderId, CancellationToken cancellationToken);

    /// <summary>Refunds the captured payment, full or partial, de-duplicated by the caller's idempotency key.</summary>
    Task<RefundReceipt> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey,
        CancellationToken cancellationToken);

    /// <summary>The caller's own orders with their payment state.</summary>
    Task<IReadOnlyList<PaymentView>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken);

    /// <summary>Lines up PayPal's transactions against eShop orders across the whole [from, to] range.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken);
}
