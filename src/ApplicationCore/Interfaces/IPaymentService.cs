using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPaymentService
{
    /// <summary>Places an order from catalog items at current catalog prices. Starts as PendingPayment.</summary>
    Task<Order> CreateOrderAsync(string buyerId, IReadOnlyList<OrderItemRequest> items, Address shipToAddress,
        CancellationToken ct = default);

    /// <summary>
    /// Authorizes (holds) the order total, either with raw card details or one of the shopper's
    /// saved cards. Returns null when the order does not exist or belongs to another shopper.
    /// Repeating the call on an already-authorized order returns the current state without re-authorizing.
    /// </summary>
    Task<Order?> PayOrderAsync(string buyerId, int orderId, CardDetails? card, int? savedPaymentMethodId,
        CancellationToken ct = default);

    /// <summary>Captures the held funds. Renews a stale authorization first; fails with an
    /// operator-actionable error when the hold can no longer be renewed. Idempotent.</summary>
    Task<Order?> FulfilOrderAsync(int orderId, CancellationToken ct = default);

    /// <summary>Releases the hold before fulfilment; no money moves. Idempotent.</summary>
    Task<Order?> CancelOrderAsync(int orderId, CancellationToken ct = default);

    /// <summary>
    /// Refunds a captured payment in full (amount == null) or in part. A repeated idempotency key
    /// returns the original refund without calling the provider again.
    /// </summary>
    Task<OrderRefund?> RefundOrderAsync(int orderId, decimal? amount, string idempotencyKey,
        CancellationToken ct = default);

    Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken ct = default);

    Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct = default);

    Task<IReadOnlyList<SavedPaymentMethod>> GetSavedCardsAsync(string buyerId, CancellationToken ct = default);

    /// <summary>Returns false when the card does not exist or belongs to another shopper.</summary>
    Task<bool> DeleteSavedCardAsync(string buyerId, int savedPaymentMethodId, CancellationToken ct = default);

    Task<ReconciliationReport> GetReconciliationAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}

public sealed record OrderItemRequest(int CatalogItemId, int Units);
