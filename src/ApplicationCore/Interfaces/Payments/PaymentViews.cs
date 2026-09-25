using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

/// <summary>Result of placing an order.</summary>
public record PlaceOrderResult(int OrderId, decimal Total, string CurrencyCode);

/// <summary>A refund view returned to the caller.</summary>
public record RefundView(string RefundId, string? Status, decimal Amount, string CurrencyCode, DateTimeOffset CreatedAt);

/// <summary>Projection of an order's payment state for API responses.</summary>
public record OrderPaymentView(
    int OrderId,
    string PaymentStatus,
    decimal OrderTotal,
    string? CurrencyCode,
    string? ReferenceId,
    decimal? AuthorizedAmount,
    string? PayPalOrderId,
    string? AuthorizationId,
    string? AuthorizationStatus,
    DateTimeOffset? AuthorizationExpiresAt,
    string? CaptureId,
    string? CaptureStatus,
    decimal? CapturedAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    decimal TotalRefunded,
    IReadOnlyList<RefundView> Refunds);

/// <summary>A safe view of a saved card — never full card details.</summary>
public record SavedCardView(
    string PaymentMethodId,
    string? Brand,
    string? Last4,
    string? Expiry,
    string? CardholderName,
    DateTimeOffset CreatedAt);

// --- Reconciliation ---

public record ReconciliationMatch(
    int OrderId,
    string ReferenceId,
    string EShopPaymentStatus,
    decimal EShopCapturedAmount,
    string? PayPalTransactionId,
    string? PayPalStatus,
    decimal? PayPalAmount);

public record ReconciliationEShopOnly(int OrderId, string ReferenceId, string EShopPaymentStatus, decimal? EShopCapturedAmount);

public record ReconciliationPayPalOnly(string? TransactionId, string? Status, decimal? Amount, string? CurrencyCode, string? InvoiceId, DateTimeOffset? InitiatedAt);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string CurrencyCode,
    int PayPalTransactionCount,
    int EShopOrderCount,
    bool Complete,
    int PagesScanned,
    int TotalPages,
    IReadOnlyList<ReconciliationMatch> Matched,
    IReadOnlyList<ReconciliationEShopOnly> InEShopNotInPayPal,
    IReadOnlyList<ReconciliationPayPalOnly> InPayPalNotInEShop);
