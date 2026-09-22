using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.PaymentGateway;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A requested catalog line for a new order.</summary>
public sealed record OrderLineInput(int CatalogItemId, int Quantity);

/// <summary>One line of a reconciliation report.</summary>
public sealed record ReconciliationLine(
    int? EShopOrderId,
    string? InvoiceId,
    string? PayPalTransactionId,
    string? Status,
    string? Amount,
    string? CurrencyCode,
    string? Date);

/// <summary>PayPal's records lined up against eShop orders over a date range.</summary>
public sealed record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    int PagesScanned,
    bool Truncated,
    IReadOnlyList<ReconciliationLine> Matched,
    IReadOnlyList<ReconciliationLine> OnlyInPayPal,
    IReadOnlyList<ReconciliationLine> OnlyInEShop);

/// <summary>
/// Orchestrates the pay-for-an-order flow: place, authorize (hold), fulfil (capture), cancel (void),
/// refund, list, reconcile. Each action is separately invocable and idempotent in effect.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Places an order from catalog lines, reusing the existing Order model. Returns the new order id.</summary>
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> lines, Address? shipToAddress, CancellationToken ct);

    /// <summary>Authorizes (holds) the order total, using a one-off card or a saved card. Shopper-scoped.</summary>
    Task<OrderPayment> AuthorizeAsync(string buyerId, int orderId, CardDetails? card, int? savedPaymentMethodId, CancellationToken ct);

    /// <summary>Fulfils the order: captures (takes) the held money, renewing a stale authorization first. Operator action.</summary>
    Task<OrderPayment> FulfilAsync(int orderId, CancellationToken ct);

    /// <summary>Cancels before fulfilment: releases the held funds (void). Operator action.</summary>
    Task<OrderPayment> CancelAsync(int orderId, CancellationToken ct);

    /// <summary>Refunds a captured payment in full or in part. Idempotent per <paramref name="idempotencyKey"/>. Shopper-scoped.</summary>
    Task<RefundRecord> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken ct);

    /// <summary>The caller's orders with their payment state.</summary>
    Task<IReadOnlyList<OrderPayment>> GetOrdersForBuyerAsync(string buyerId, CancellationToken ct);

    /// <summary>PayPal's transactions for a date range, lined up against eShop orders. Operator action.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
