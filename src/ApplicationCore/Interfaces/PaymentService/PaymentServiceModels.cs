using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.PaymentService;

/// <summary>A catalog line requested when placing an order via the API.</summary>
public record PlaceOrderItem(int CatalogItemId, int Units);

/// <summary>A snapshot of a payment after a pay / fulfil / cancel operation.</summary>
public record PaymentResult(
    int OrderId,
    string PaymentStatus,
    string CurrencyCode,
    string? PayPalOrderId,
    string? AuthorizationId,
    string? AuthorizationStatus,
    DateTimeOffset? AuthorizationExpiresAt,
    string? CaptureId,
    string? CaptureStatus,
    decimal? CapturedAmount,
    decimal? PayPalFee,
    decimal? NetAmount);

/// <summary>The outcome of a refund.</summary>
public record RefundResult(
    int RefundId,
    string PayPalRefundId,
    string Status,
    decimal Amount,
    decimal TotalRefunded,
    string PaymentStatus);

public record OrderLineView(int CatalogItemId, string ProductName, decimal UnitPrice, int Units);

/// <summary>An order together with its payment state, for GET /api/my-orders.</summary>
public record OrderPaymentView(
    int OrderId,
    DateTimeOffset OrderDate,
    decimal Total,
    string CurrencyCode,
    string PaymentStatus,
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
    IReadOnlyList<OrderLineView> Items);

public record SavedCardView(
    int PaymentMethodId,
    string Brand,
    string LastFourDigits,
    string Expiry,
    DateTimeOffset SavedAt);

/// <summary>One reconciliation line: a PayPal transaction, an eShop payment, or both matched.</summary>
public record ReconciliationRow(
    string MatchState,
    int? OrderId,
    string? PayPalTransactionId,
    string? PayPalInvoiceId,
    string? PayPalCustomField,
    decimal? PayPalAmount,
    string? PayPalStatus,
    decimal? EShopAmount,
    string? EShopPaymentStatus);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    int EShopPaymentCount,
    int MatchedCount,
    int InPayPalNotEShopCount,
    int InEShopNotPayPalCount,
    IReadOnlyList<ReconciliationRow> Rows);
