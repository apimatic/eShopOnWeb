using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the money movement that follows an order: placing it (awaiting payment),
/// authorizing the total (hold), fulfilling (capture), cancelling (void) and refunding — plus the
/// caller's own order view and the operator reconciliation report. Each action is separately
/// invocable; nothing here pays-fulfils-refunds behind a single call.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Place an order from catalog items for the given shopper. Returns the new order id.</summary>
    Task<int> PlaceOrderAsync(string buyerId, IEnumerable<OrderLineInput> lines, Address shipToAddress, CancellationToken cancellationToken = default);

    /// <summary>Authorize (hold) the order total. Idempotent in effect — a double-click never authorizes twice.</summary>
    Task AuthorizeAsync(string buyerId, int orderId, PayOrderInput payment, CancellationToken cancellationToken = default);

    /// <summary>Operator: fulfil the order — captures the money, renewing a stale hold first if needed.</summary>
    Task FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Operator: cancel before fulfilment — releases any held funds.</summary>
    Task CancelAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Refund the caller's own captured order, in full (amount null) or in part. Returns the refund id.</summary>
    Task<string> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>The caller's own orders with their payment state.</summary>
    Task<IReadOnlyList<OrderPaymentView>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Operator: PayPal's transactions for a range, lined up against eShop orders.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

/// <summary>One catalog line for a placed order; the price comes from the catalog, not the caller.</summary>
public record OrderLineInput(int CatalogItemId, int Quantity);

/// <summary>How to pay an order: raw card details, or one of the shopper's saved cards by id.</summary>
public record PayOrderInput(CardDetails? Card, int? SavedPaymentMethodId);

public record OrderPaymentRefundView(string RefundId, decimal Amount, string Status, DateTimeOffset CreatedAt);

/// <summary>The caller-facing view of an order and its payment state.</summary>
public record OrderPaymentView(
    int OrderId,
    DateTimeOffset OrderDate,
    decimal Total,
    string CurrencyCode,
    string Status,
    string? PayPalOrderId,
    string? AuthorizationId,
    string? AuthorizationStatus,
    DateTimeOffset? AuthorizationExpiresAt,
    string? CaptureId,
    string? CaptureStatus,
    decimal? CapturedAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    decimal RefundedAmount,
    IReadOnlyList<OrderPaymentRefundView> Refunds,
    IReadOnlyList<OrderLineView> Items);

public record OrderLineView(int CatalogItemId, string ProductName, decimal UnitPrice, int Units);

/// <summary>Where a settlement was seen: in both systems, only at PayPal, or only in eShop.</summary>
public enum ReconciliationMatch
{
    Matched,
    InPayPalOnly,
    InEShopOnly
}

public record ReconciliationLine(
    ReconciliationMatch Match,
    string TransactionId,
    decimal? Amount,
    string? CurrencyCode,
    string? PayPalStatus,
    DateTimeOffset? PayPalDate,
    int? OrderId,
    string? EShopStatus);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    int MatchedCount,
    int InPayPalOnlyCount,
    int InEShopOnlyCount,
    IReadOnlyList<ReconciliationLine> Lines);
