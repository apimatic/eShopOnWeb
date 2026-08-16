using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the order + payment lifecycle: place an order, authorize (hold) its total,
/// fulfil (capture), cancel (void) or refund it, list a shopper's orders, and reconcile
/// against PayPal's transaction records. All shopper-scoped methods act only on the caller's
/// own data; operator-scoped methods (fulfil, cancel, reconcile) are gated at the API layer.
/// </summary>
public interface IPaymentService
{
    /// <summary>Place an order from catalog items for a shopper. Returns the new order id.</summary>
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyCollection<PlaceOrderItem> items,
        ShippingAddressInput? address, CancellationToken cancellationToken = default);

    /// <summary>
    /// Authorize the order total: place a hold equal to the total, using either inline card
    /// details or one of the shopper's saved cards. Idempotent — a repeat returns the existing hold.
    /// </summary>
    Task<OrderPaymentView> AuthorizeAsync(string buyerId, int orderId, CardDetails? card,
        int? savedCardId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fulfil the order: capture the held funds. Renews a stale authorization when possible,
    /// and reports clearly when it can no longer be renewed. Idempotent — a repeat returns the
    /// existing capture. Operator action.
    /// </summary>
    Task<OrderPaymentView> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Cancel before fulfilment: release the held funds. Idempotent. Operator action.</summary>
    Task<OrderPaymentView> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refund a captured payment, in full or in part, guarded so the total refunded never
    /// exceeds what was captured. The idempotency key deduplicates repeats.
    /// </summary>
    Task<RefundOutcome> RefundAsync(string buyerId, int orderId, decimal? amount,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>The caller's orders with their payment state.</summary>
    Task<IReadOnlyList<OrderPaymentView>> GetMyOrdersAsync(string buyerId,
        CancellationToken cancellationToken = default);

    /// <summary>A single order owned by the caller, or null if it does not exist / is not theirs.</summary>
    Task<OrderPaymentView?> GetMyOrderAsync(string buyerId, int orderId,
        CancellationToken cancellationToken = default);

    /// <summary>Reconcile PayPal's transaction records against eShop orders for a date range. Operator action.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
