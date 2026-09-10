using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>A shopper-facing view of an order together with its payment state.</summary>
public record OrderPaymentView(
    int OrderId,
    DateTimeOffset OrderDate,
    string BuyerId,
    decimal Total,
    string Currency,
    PaymentStatus Status,
    string? PayPalOrderId,
    string? AuthorizationId,
    string? AuthorizationExpiresAt,
    string? CaptureId,
    decimal? CapturedAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    decimal RefundedAmount,
    string? LastError,
    IReadOnlyList<OrderPaymentLineView> Items,
    IReadOnlyList<RefundView> Refunds);

public record OrderPaymentLineView(int CatalogItemId, string ProductName, decimal UnitPrice, int Units);

public record RefundView(string RefundId, decimal Amount, string Status, DateTimeOffset CreatedAt);

/// <summary>A saved card, described safely (never the full number).</summary>
public record SavedCardView(int Id, string Brand, string LastDigits, string? Expiry, string? CardholderName, DateTimeOffset CreatedAt);

/// <summary>Result of a refund request.</summary>
public record RefundResult(string RefundId, decimal Amount, string Status);

/// <summary>Result of placing an order.</summary>
public record PlaceOrderResult(int OrderId, decimal Total, string Currency, PaymentStatus Status);

/// <summary>An item to order: a catalog item id and a quantity.</summary>
public record OrderLineInput(int CatalogItemId, int Quantity);

// --- Reconciliation ---

/// <summary>A reconciliation report lining PayPal's transactions up against eShop orders over a range.</summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    int MatchedCount,
    IReadOnlyList<ReconciliationMatch> Matched,
    IReadOnlyList<PayPalTransaction> InPayPalOnly,
    IReadOnlyList<ReconciliationOrder> InEShopOnly);

/// <summary>A PayPal transaction matched to an eShop order (by invoice id = order id).</summary>
public record ReconciliationMatch(int OrderId, PaymentStatus EShopStatus, decimal EShopAmount, PayPalTransaction PayPalTransaction);

/// <summary>An eShop order with captured money that has no matching PayPal transaction in the range.</summary>
public record ReconciliationOrder(int OrderId, PaymentStatus Status, decimal Amount, string Currency, string? CaptureId);
