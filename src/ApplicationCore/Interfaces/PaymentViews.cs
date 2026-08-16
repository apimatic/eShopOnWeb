using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A single refund as shown to a caller.</summary>
public record RefundView(string RefundId, decimal Amount, string Currency, string Status, DateTimeOffset CreatedAt);

/// <summary>The payment state of an order, surfaced through the API.</summary>
public record PaymentView(
    int OrderId,
    string Status,
    string Currency,
    decimal Amount,
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
    decimal RefundableRemaining,
    string? FailureReason,
    IReadOnlyList<RefundView> Refunds);

/// <summary>One item line of an order.</summary>
public record OrderItemView(int CatalogItemId, string ProductName, decimal UnitPrice, int Units);

/// <summary>An order together with its payment state, for <c>GET /api/my-orders</c>.</summary>
public record OrderSummaryView(
    int OrderId,
    DateTimeOffset OrderDate,
    decimal Total,
    string PaymentStatus,
    PaymentView? Payment,
    IReadOnlyList<OrderItemView> Items);

/// <summary>A saved card, described safely — never full card details.</summary>
public record SavedCardView(int PaymentMethodId, string Brand, string Last4, string Expiry, string? CardholderName, DateTimeOffset CreatedAt);

/// <summary>How a reconciliation line matched up between PayPal and eShop.</summary>
public enum ReconciliationMatch
{
    Matched,
    InPayPalOnly,
    InEShopOnly
}

public record ReconciliationLine(
    ReconciliationMatch Match,
    string? PayPalTransactionId,
    int? OrderId,
    string? EShopCaptureId,
    decimal? PayPalAmount,
    decimal? EShopAmount,
    string? Currency,
    string? Status,
    DateTimeOffset? TransactionDate);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    int EShopPaymentCount,
    int MatchedCount,
    int InPayPalOnlyCount,
    int InEShopOnlyCount,
    IReadOnlyList<ReconciliationLine> Lines);
