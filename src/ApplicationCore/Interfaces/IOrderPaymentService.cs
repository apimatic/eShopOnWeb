using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>One requested catalog line for a new order.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

/// <summary>An order together with its payment/fulfilment state, for the "my orders" view.</summary>
public record OrderPaymentView(Order Order, Payment Payment);

/// <summary>One line of the reconciliation report: a PayPal transaction, an eShop payment, or both.</summary>
public record ReconciliationEntry(
    string MatchStatus,
    string? PayPalTransactionId,
    string? PayPalStatus,
    decimal? PayPalAmount,
    string? PayPalCurrency,
    DateTimeOffset? PayPalDate,
    int? OrderId,
    decimal? EShopAmount,
    string? EShopStatus);

/// <summary>The whole reconciliation report over a date range.</summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    int EShopPaymentCount,
    int MatchedCount,
    int PayPalOnlyCount,
    int EShopOnlyCount,
    IReadOnlyList<ReconciliationEntry> Entries);

/// <summary>
/// Orchestrates the money movement over the existing order model: place, authorize (hold),
/// fulfil (capture), cancel (void), refund, plus the shopper and operator read views.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>
    /// Place an order from catalog lines for the given buyer, creating its awaiting-payment
    /// record. Returns the new payment (carrying the order id, total and currency).
    /// </summary>
    Task<Payment> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines, Address shipToAddress,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Authorize the order total using either raw card details or one of the buyer's saved cards.
    /// Idempotent: authorizing an already-authorized order returns the existing hold.
    /// </summary>
    Task<Payment> AuthorizeAsync(string buyerId, int orderId, CardDetails? card, int? savedPaymentMethodId,
        CancellationToken cancellationToken = default);

    /// <summary>Operator: fulfil the order, capturing the money. Renews a stale hold before capture.</summary>
    Task<Payment> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Operator: cancel before fulfilment, voiding the hold so no money moves.</summary>
    Task<Payment> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Refund a captured payment in full or in part, under a caller idempotency key.</summary>
    Task<PaymentRefund> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>The buyer's own orders with their payment state.</summary>
    Task<IReadOnlyList<OrderPaymentView>> GetOrdersForBuyerAsync(string buyerId,
        CancellationToken cancellationToken = default);

    /// <summary>Operator: reconcile PayPal's transaction record against eShop payments over a range.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
