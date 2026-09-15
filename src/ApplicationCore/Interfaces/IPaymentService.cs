using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the money movement around an order: place → authorize (hold) → fulfil (capture) →
/// cancel (release) / refund. Also manages a shopper's saved cards and operator reconciliation.
/// Each action is separately invocable; none does more than its name.
/// </summary>
public interface IPaymentService
{
    /// <summary>Place an order from catalog items for a shopper. Returns the new order id.</summary>
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, Address shipToAddress,
        CancellationToken ct);

    /// <summary>Authorize (hold) the order total, paying with a one-off card or a saved card. Idempotent.</summary>
    Task<Payment> AuthorizeOrderAsync(int orderId, string buyerId, PaymentInstruction instruction,
        CancellationToken ct);

    /// <summary>Operator: fulfil the order — capture the held funds, renewing a stale hold if needed.</summary>
    Task<Payment> FulfilOrderAsync(int orderId, CancellationToken ct);

    /// <summary>Operator: cancel before fulfilment — release the held funds.</summary>
    Task<Payment> CancelOrderAsync(int orderId, CancellationToken ct);

    /// <summary>Operator: refund a captured payment, full or partial, deduped by the caller's idempotency key.</summary>
    Task<Refund> RefundOrderAsync(int orderId, decimal? amount, string idempotencyKey, CancellationToken ct);

    /// <summary>The caller's own orders with their payment state.</summary>
    Task<IReadOnlyList<Payment>> GetMyOrdersAsync(string buyerId, CancellationToken ct);

    /// <summary>Save (vault) a card for the shopper. Returns the saved card (safe descriptor only).</summary>
    Task<SavedCard> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct);

    /// <summary>The caller's saved cards.</summary>
    Task<IReadOnlyList<SavedCard>> GetSavedCardsAsync(string buyerId, CancellationToken ct);

    /// <summary>Remove one of the caller's saved cards, and delete it from the PayPal vault.</summary>
    Task DeleteSavedCardAsync(string buyerId, int savedCardId, CancellationToken ct);

    /// <summary>Operator: reconcile PayPal's transactions for a date range against eShop orders.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
