using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the additive payment flows on top of the existing order model: placing an order, holding and
/// taking money, cancelling, refunding, and managing saved cards. Enforces idempotency, refund caps and
/// per-shopper ownership.
/// </summary>
public interface IPaymentService
{
    // Flow 1 — pay for an order
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines,
        ShippingAddressInput? shippingAddress, CancellationToken ct = default);

    Task<OrderPayment> PayAsync(string buyerId, int orderId, PaymentInstruction instruction,
        CancellationToken ct = default);

    Task<OrderPayment> FulfilAsync(int orderId, CancellationToken ct = default);

    Task<OrderPayment> CancelAsync(int orderId, CancellationToken ct = default);

    Task<(OrderPayment Payment, PaymentRefund Refund)> RefundAsync(string buyerId, int orderId,
        decimal? amount, string idempotencyKey, CancellationToken ct = default);

    Task<IReadOnlyList<MyOrder>> GetMyOrdersAsync(string buyerId, CancellationToken ct = default);

    // Flow 2 — saved cards
    Task<SavedCardResult> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct = default);

    Task<IReadOnlyList<SavedCardResult>> ListSavedCardsAsync(string buyerId, CancellationToken ct = default);

    Task DeleteSavedCardAsync(string buyerId, string paymentMethodId, CancellationToken ct = default);

    // Reconciliation (operator)
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct = default);
}
