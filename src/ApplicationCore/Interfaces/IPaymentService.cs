using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the money movement for an order: place -> authorize (hold) -> fulfil (capture)
/// -> cancel (release) / refund (give back), plus reconciliation against PayPal.
/// Each action is separately invocable and idempotent in effect.
/// </summary>
public interface IPaymentService
{
    /// <summary>Places an order (reusing the existing Order/OrderItem model) awaiting payment. Returns the new order id.</summary>
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> lines, Address shipToAddress, CancellationToken cancellationToken = default);

    /// <summary>Authorizes (holds) the order total. Idempotent: a double request never authorizes twice.</summary>
    Task<Payment> AuthorizeAsync(string buyerId, int orderId, PayOrderRequest request, CancellationToken cancellationToken = default);

    /// <summary>Operator action: fulfils the order, capturing the held funds (renewing a stale hold first if needed).</summary>
    Task<Payment> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: cancels before fulfilment, releasing the held funds so no money moved.</summary>
    Task<Payment> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Refunds a captured payment in full or in part, safe under a repeated idempotency key. Returns the refund.</summary>
    Task<Refund> RefundAsync(string buyerId, int orderId, string idempotencyKey, decimal? amount, CancellationToken cancellationToken = default);

    /// <summary>The caller's own orders with their payment state.</summary>
    Task<IReadOnlyList<OrderPaymentView>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: reconciles PayPal's transactions against eShop orders over a date range.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
