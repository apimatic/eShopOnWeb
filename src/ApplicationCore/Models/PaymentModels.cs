using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Models;

/// <summary>A single line requested when placing an order: a catalog item and how many.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>
/// Pay for an order either with one-off card details or with one of the shopper's saved cards.
/// Exactly one of the two must be supplied.
/// </summary>
public record PayOrderRequest(CardDetails? Card, int? SavedPaymentMethodId);

public record RefundView(int RefundId, string? PayPalRefundId, decimal Amount, string Status, DateTimeOffset CreatedDate);

public record OrderLineView(int CatalogItemId, string ProductName, decimal UnitPrice, int Units);

/// <summary>An order together with its payment state, for the shopper's "my orders" view.</summary>
public record OrderPaymentView(
    int OrderId,
    DateTimeOffset OrderDate,
    decimal Total,
    string CurrencyCode,
    string PaymentStatus,
    string? PayPalOrderId,
    string? AuthorizationId,
    string? CaptureId,
    decimal? CapturedGross,
    decimal? PayPalFee,
    decimal? NetAmount,
    string? CardDescriptor,
    string? OperatorMessage,
    IReadOnlyList<OrderLineView> Lines,
    IReadOnlyList<RefundView> Refunds);

/// <summary>A PayPal transaction lined up against an eShop order (MatchedOrderId is null if no match).</summary>
public record ReconciliationEntry(
    string TransactionId,
    string? Status,
    decimal? Amount,
    string? CurrencyCode,
    decimal? FeeAmount,
    string? InvoiceId,
    string? CustomField,
    string? EventCode,
    DateTimeOffset? Date,
    int? MatchedOrderId);

/// <summary>An eShop payment that has PayPal activity but was not found in PayPal's report for the range.</summary>
public record EshopUnmatchedPayment(
    int OrderId,
    string InvoiceId,
    string PaymentStatus,
    string? AuthorizationId,
    string? CaptureId,
    decimal Amount,
    string CurrencyCode);

/// <summary>
/// Reconciliation over a date range: PayPal's own transactions (each annotated with the matched
/// eShop order, or null when eShop has no record) and eShop payments PayPal did not report.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    int EshopPaymentCount,
    int MatchedCount,
    IReadOnlyList<ReconciliationEntry> Transactions,
    IReadOnlyList<EshopUnmatchedPayment> InEshopNotInPayPal);

public record SavedCardView(
    int PaymentMethodId,
    string Brand,
    string Last4,
    string Expiry,
    string Descriptor,
    DateTimeOffset CreatedDate);
