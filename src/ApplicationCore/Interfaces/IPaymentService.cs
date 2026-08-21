using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PaymentGateway;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PaymentService;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the money movement around an order: place, authorize (hold), fulfil (capture),
/// cancel (release) and refund; plus a shopper's saved cards and the reconciliation report. Each
/// action is separately invocable and idempotent in effect. Shopper-scoped methods take the caller's
/// <c>buyerId</c> and act only on that shopper's own data; operator methods do not.
/// </summary>
public interface IPaymentService
{
    // ---- Shopper: place & pay ----
    Task<int> PlaceOrderAsync(string buyerId, Address shipToAddress, IReadOnlyCollection<PlaceOrderItem> items, CancellationToken ct = default);
    Task<PaymentResult> AuthorizeOrderAsync(string buyerId, int orderId, CardDetails? card, int? savedPaymentMethodId, CancellationToken ct = default);
    Task<IReadOnlyList<OrderPaymentView>> GetMyOrdersAsync(string buyerId, CancellationToken ct = default);
    Task<RefundResult> RefundOrderAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken ct = default);

    // ---- Operator ----
    Task<PaymentResult> FulfilOrderAsync(int orderId, CancellationToken ct = default);
    Task<PaymentResult> CancelOrderAsync(int orderId, CancellationToken ct = default);
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);

    // ---- Shopper: saved cards ----
    Task<SavedCardView> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct = default);
    Task<IReadOnlyList<SavedCardView>> GetSavedCardsAsync(string buyerId, CancellationToken ct = default);
    Task DeleteSavedCardAsync(string buyerId, int paymentMethodId, CancellationToken ct = default);
}
