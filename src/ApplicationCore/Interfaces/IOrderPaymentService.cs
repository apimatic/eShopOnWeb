using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.PaymentGateway;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A single line requested when placing an order.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>
/// Flow 1 — placing an order and moving money through its lifecycle: authorize (hold),
/// fulfil (capture), cancel (void), refund, plus queries and reconciliation.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Place an order (awaiting payment) from catalog items for the given shopper.</summary>
    Task<Order> PlaceOrderAsync(string identity, IReadOnlyList<OrderLineRequest> lines, Address shipToAddress,
        CancellationToken cancellationToken = default);

    /// <summary>Authorize (hold) the order total with a one-off card.</summary>
    Task<Order> PayWithCardAsync(string identity, int orderId, CardDetails card,
        CancellationToken cancellationToken = default);

    /// <summary>Authorize (hold) the order total with one of the shopper's saved cards.</summary>
    Task<Order> PayWithSavedCardAsync(string identity, int orderId, int paymentMethodId,
        CancellationToken cancellationToken = default);

    /// <summary>Operator: fulfil the order and capture the held funds (re-authorizing if stale).</summary>
    Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Operator: cancel before fulfilment, releasing any held funds.</summary>
    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Refund a captured order, in full or in part, idempotently by key.</summary>
    Task<Refund> RefundAsync(string identity, int orderId, decimal? amount, string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>The caller's orders, with payment state.</summary>
    Task<IReadOnlyList<Order>> GetMyOrdersAsync(string identity, CancellationToken cancellationToken = default);

    /// <summary>Operator: reconcile PayPal's transaction records against eShop orders over a range.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}

// ----------------------------------------------------------------- Reconciliation result types

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<MatchedReconciliationItem> Matched,
    IReadOnlyList<PayPalOnlyReconciliationItem> PayPalOnly,
    IReadOnlyList<EShopOnlyReconciliationItem> EShopOnly);

/// <summary>A PayPal transaction that eShop also knows about (matched by transaction id).</summary>
public record MatchedReconciliationItem(
    string TransactionId,
    string RecordType, // "capture" | "refund"
    int OrderId,
    string? PayPalStatus,
    decimal? PayPalAmount,
    string? EShopStatus,
    decimal? EShopAmount);

/// <summary>A transaction PayPal knows about but eShop does not.</summary>
public record PayPalOnlyReconciliationItem(
    string TransactionId,
    string? Status,
    decimal? Amount,
    string? CurrencyCode,
    DateTimeOffset? InitiationDate,
    string? EventCode);

/// <summary>A money movement eShop recorded but PayPal's report (for the range) does not show.</summary>
public record EShopOnlyReconciliationItem(
    string TransactionId,
    string RecordType, // "capture" | "refund"
    int OrderId,
    string? Status,
    decimal Amount);
