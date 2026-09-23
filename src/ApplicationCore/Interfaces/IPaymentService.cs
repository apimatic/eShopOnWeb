using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the order-payment flow: place, authorize (hold), fulfil (capture), cancel (void), refund,
/// list, and reconcile. Each action is separately invocable. Shopper-scoped actions act only on the
/// caller's own orders; fulfil/cancel/reconcile are operator actions gated at the API layer.
/// </summary>
public interface IPaymentService
{
    /// <summary>Places an order from catalog items for the buyer and returns the new order id (awaiting payment).</summary>
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> lines, AddressInput? shipTo, CancellationToken ct);

    /// <summary>Authorizes (holds) the order total. Idempotent: a repeat returns the existing authorization.</summary>
    Task<PaymentView> AuthorizeAsync(string buyerId, int orderId, PayInput pay, CancellationToken ct);

    /// <summary>Operator: fulfils the order, capturing the money. Renews a stale authorization first if needed.</summary>
    Task<PaymentView> FulfilAsync(int orderId, CancellationToken ct);

    /// <summary>Operator: cancels before fulfilment, releasing the held funds.</summary>
    Task<PaymentView> CancelAsync(int orderId, CancellationToken ct);

    /// <summary>Refunds a captured payment, full or partial. The idempotency key prevents a repeat from refunding twice.</summary>
    Task<RefundView> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken ct);

    /// <summary>The caller's orders with their payment state.</summary>
    Task<IReadOnlyList<PaymentView>> GetMyOrdersAsync(string buyerId, CancellationToken ct);

    /// <summary>Operator: PayPal's transactions for a date range lined up against eShop orders.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
