using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The application façade for the additive payment capability. Each method is separately invocable —
/// place, pay, fulfil, cancel, refund, report — and every shopper-scoped call acts only on the
/// caller's own data (enforced by <paramref name="buyerId"/>). Operator actions take no buyer.
/// </summary>
public interface IPaymentService
{
    // ---- Flow 1: pay for an order ----

    /// <summary>Place an order from catalog items for the caller. Amounts come from catalog prices.</summary>
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<PlaceOrderItem> items, ShippingAddressInput? shipTo, CancellationToken ct);

    /// <summary>Authorize (hold) the order total. Idempotent: a double-click never authorizes twice.</summary>
    Task<OrderPaymentView> PayAsync(string buyerId, int orderId, PayCommand command, CancellationToken ct);

    /// <summary>Operator: fulfil the order and capture the money, renewing a stale hold if needed.</summary>
    Task<OrderPaymentView> FulfilAsync(int orderId, CancellationToken ct);

    /// <summary>Operator: cancel before fulfilment, releasing the held funds.</summary>
    Task<OrderPaymentView> CancelAsync(int orderId, CancellationToken ct);

    /// <summary>Refund a captured payment in full or in part, under a caller-supplied idempotency key.</summary>
    Task<RefundResult> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken ct);

    /// <summary>The caller's orders with their payment state.</summary>
    Task<IReadOnlyList<OrderPaymentView>> GetMyOrdersAsync(string buyerId, CancellationToken ct);

    /// <summary>Operator: reconcile PayPal's transactions against eShop orders over a date range.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);

    // ---- Flow 2: saved cards ----

    /// <summary>Save (vault) a card for the caller.</summary>
    Task<SavedCardView> SaveCardAsync(string buyerId, SaveCardCommand command, CancellationToken ct);

    /// <summary>The caller's saved cards.</summary>
    Task<IReadOnlyList<SavedCardView>> GetSavedCardsAsync(string buyerId, CancellationToken ct);

    /// <summary>Remove one of the caller's saved cards; afterwards it is neither listed nor usable to pay.</summary>
    Task RemoveSavedCardAsync(string buyerId, int paymentMethodId, CancellationToken ct);
}
