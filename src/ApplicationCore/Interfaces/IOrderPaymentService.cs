using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the pay-for-an-order flow: placing an order, authorizing (holding) the money, capturing it
/// at fulfilment, cancelling before fulfilment, refunding after, and reconciling against PayPal.
/// Shopper-scoped operations act only on the caller's own orders; operator operations (fulfil, cancel,
/// reconcile) are authorized at the endpoint.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Places an order from catalog lines for the shopper. The order starts awaiting payment;
    /// the returned summary carries the new order id and total.</summary>
    Task<OrderPaymentSummary> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> lines, ShippingAddressInput address, CancellationToken ct = default);

    /// <summary>Authorizes (holds) the order total. Idempotent: a repeat does not authorize twice.</summary>
    Task<OrderPaymentSummary> AuthorizeAsync(string buyerId, int orderId, PaymentInstrument instrument, CancellationToken ct = default);

    /// <summary>Operator: marks the order fulfilled and captures the held funds. Idempotent.</summary>
    Task<OrderPaymentSummary> FulfilAsync(int orderId, CancellationToken ct = default);

    /// <summary>Operator: cancels before fulfilment, releasing the held funds. Idempotent.</summary>
    Task<OrderPaymentSummary> CancelAsync(int orderId, CancellationToken ct = default);

    /// <summary>Refunds the caller's captured order, in full or in part, under a caller-supplied idempotency
    /// key. Returns the payment summary and the refund id via <paramref name="refundId"/>.</summary>
    Task<(OrderPaymentSummary Summary, string RefundId)> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken ct = default);

    /// <summary>The caller's own orders with their payment state.</summary>
    Task<IReadOnlyList<OrderPaymentSummary>> GetMyOrdersAsync(string buyerId, CancellationToken ct = default);

    /// <summary>Operator: PayPal's transactions for a date range lined up against eShop orders.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
