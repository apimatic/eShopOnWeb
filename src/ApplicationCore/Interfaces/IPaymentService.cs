using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Business flows for order payments: authorize at checkout, capture at
/// fulfilment, void on cancel, refund on return, plus saved-card management
/// and reconciliation against PayPal's transaction report.
/// </summary>
public interface IPaymentService
{
    /// <summary>
    /// Authorize the order total with either raw card details or one of the
    /// shopper's saved cards. Idempotent: paying an already-authorized order
    /// returns the existing payment. Returns null when the order does not
    /// exist or does not belong to the buyer.
    /// </summary>
    Task<Payment?> PayOrderAsync(string buyerId, int orderId, CardDetails? card, int? savedCardId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Capture the authorized funds (operator action at fulfilment). Renews a
    /// stale authorization when possible. Idempotent: fulfilling an
    /// already-fulfilled order returns the existing payment.
    /// </summary>
    Task<Payment?> CaptureOrderPaymentAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Release the held funds without taking any money (operator action,
    /// before fulfilment). Idempotent.
    /// </summary>
    Task<Payment?> CancelOrderPaymentAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refund a captured payment, in full or in part. The idempotency key is
    /// caller-supplied; repeating the same key returns the original refund.
    /// Returns null when the order does not exist or does not belong to the buyer.
    /// </summary>
    Task<PaymentRefund?> RefundOrderPaymentAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, string? noteToPayer, CancellationToken cancellationToken = default);

    Task<SavedCard> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedCard>> ListSavedCardsAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Returns false when the card does not exist or belongs to another shopper.</summary>
    Task<bool> DeleteSavedCardAsync(string buyerId, int savedCardId, CancellationToken cancellationToken = default);

    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
