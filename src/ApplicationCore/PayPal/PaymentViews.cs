using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.PayPal;

/// <summary>A catalog line for placing an order: which item and how many.</summary>
public record OrderLineInput(int CatalogItemId, int Quantity);

/// <summary>Optional shipping address for a placed order (shipping is not the focus of payments).</summary>
public record ShippingAddressInput(
    string Street,
    string City,
    string State,
    string Country,
    string ZipCode);

/// <summary>An order with its current payment state, as returned to the shopper/operator.</summary>
public record OrderPaymentView(
    int OrderId,
    string BuyerId,
    string PaymentStatus,
    string Currency,
    decimal Amount,
    decimal? CapturedAmount,
    decimal? PaypalFee,
    decimal? NetAmount,
    decimal RefundedAmount,
    string? PayPalOrderId,
    string? AuthorizationId,
    string? CaptureId,
    DateTimeOffset OrderDate,
    IReadOnlyList<OrderLineView> Items);

public record OrderLineView(int CatalogItemId, string ProductName, decimal UnitPrice, int Units);

/// <summary>The outcome of a refund.</summary>
public record RefundView(
    string RefundId,
    int OrderId,
    decimal Amount,
    string Currency,
    decimal RefundedTotal,
    string PaymentStatus);

/// <summary>A saved card, described safely (never full card details).</summary>
public record SavedCardView(
    int PaymentMethodId,
    string? Brand,
    string? LastFourDigits,
    string? Expiry,
    DateTimeOffset CreatedDate);

/// <summary>One line of the reconciliation report: a PayPal transaction lined up against an eShop order.</summary>
public record ReconciliationLine(
    string MatchStatus,          // Matched | MissingInEShop | MissingInPayPal | AmountMismatch
    string? InvoiceId,
    int? OrderId,
    string? PayPalTransactionId,
    decimal? PayPalAmount,
    decimal? EShopAmount,
    string? Currency,
    string? PayPalStatus);

/// <summary>The reconciliation report over a date range.</summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    int EShopPaymentCount,
    int MatchedCount,
    int MissingInEShopCount,
    int MissingInPayPalCount,
    int AmountMismatchCount,
    IReadOnlyList<ReconciliationLine> Lines);
