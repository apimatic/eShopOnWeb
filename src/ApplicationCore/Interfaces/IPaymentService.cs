using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the additive payment capability over the existing order model: place, pay (authorize),
/// fulfil (capture), cancel (void), refund, plus saved cards and reconciliation. Every operation is
/// scoped to a shopper except the operator ones, and all payment operations are idempotent in effect.
/// </summary>
public interface IPaymentService
{
    /// <summary>Place an order from catalog items for the shopper. Starts awaiting payment. Returns the order id.</summary>
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> lines, ShippingAddressInput? shippingAddress, CancellationToken ct);

    /// <summary>Authorize (hold) the order total. Idempotent: a repeat does not authorize twice.</summary>
    Task<OrderPaymentView> PayAsync(string buyerId, int orderId, PayInstruction instruction, CancellationToken ct);

    /// <summary>Operator: fulfil the order — capture the held funds, renewing a stale authorization first if needed.</summary>
    Task<OrderPaymentView> FulfilAsync(int orderId, CancellationToken ct);

    /// <summary>Operator: cancel before fulfilment — release the hold. Idempotent.</summary>
    Task<OrderPaymentView> CancelAsync(int orderId, CancellationToken ct);

    /// <summary>Refund a fulfilled order, full or partial, without exceeding the captured amount. Idempotent per key.</summary>
    Task<RefundOutcome> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken ct);

    /// <summary>The caller's orders with their payment state.</summary>
    Task<IReadOnlyList<OrderPaymentView>> GetMyOrdersAsync(string buyerId, CancellationToken ct);

    /// <summary>Save (vault) a card for the shopper. Returns the saved-card id.</summary>
    Task<SavedCardView> SaveCardAsync(string buyerId, CardInput card, CancellationToken ct);

    /// <summary>The caller's saved cards.</summary>
    Task<IReadOnlyList<SavedCardView>> GetSavedCardsAsync(string buyerId, CancellationToken ct);

    /// <summary>Remove a saved card: it can no longer appear among the caller's cards nor be used to pay.</summary>
    Task DeleteSavedCardAsync(string buyerId, int paymentMethodId, CancellationToken ct);

    /// <summary>Operator: reconcile PayPal's transactions against eShop orders over a date range.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
