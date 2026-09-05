using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Orchestrates orders, payments and saved cards: places orders from catalog items,
/// authorizes/captures/voids/refunds through <see cref="IPaymentGateway"/>, and vaults cards.
/// </summary>
public interface IPaymentService
{
    /// <summary>Places an order from catalog items for the buyer; the order starts awaiting payment.</summary>
    Task<PlaceOrderResult> PlaceOrderAsync(string buyerId, PlaceOrderInput input, CancellationToken ct = default);

    /// <summary>
    /// Authorizes the order total with a raw card or a saved card. Idempotent in effect:
    /// an already-authorized order is returned as-is, an in-flight attempt is refused.
    /// </summary>
    Task<PayOrderResult> PayOrderAsync(string buyerId, int orderId, int? paymentMethodId, CardInput? card,
        CancellationToken ct = default);

    /// <summary>Operator action: captures the held funds, renewing a stale authorization when possible.</summary>
    Task<OperatorActionResult> FulfilOrderAsync(int orderId, CancellationToken ct = default);

    /// <summary>Operator action: cancels the order before fulfilment and releases the hold.</summary>
    Task<OperatorActionResult> CancelOrderAsync(int orderId, CancellationToken ct = default);

    /// <summary>
    /// Operator action: refunds the captured payment in full or in part, keyed by
    /// <paramref name="idempotencyKey"/> so a repeated request never refunds twice.
    /// </summary>
    Task<RefundAction> RefundOrderAsync(int orderId, decimal? amount, string idempotencyKey,
        CancellationToken ct = default);

    /// <summary>The buyer's own orders with their payment state.</summary>
    Task<IReadOnlyList<MyOrderView>> GetMyOrdersAsync(string buyerId, CancellationToken ct = default);

    /// <summary>Vaults a card for the buyer; only the token and display data are stored.</summary>
    Task<CardActionResult> SaveCardAsync(string buyerId, CardInput card, CancellationToken ct = default);

    /// <summary>The buyer's own saved cards.</summary>
    Task<IReadOnlyList<SavedCard>> GetSavedCardsAsync(string buyerId, CancellationToken ct = default);

    /// <summary>Deletes one of the buyer's saved cards so it can no longer be used to pay.</summary>
    Task<CardActionResult> DeleteSavedCardAsync(string buyerId, int paymentMethodId, CancellationToken ct = default);

    /// <summary>Operator action: lines up the provider's transactions for a range against shop payments.</summary>
    Task<GatewayResult<ReconciliationReport>> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct = default);
}
