using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>One catalog line on a place-order request. The unit price is taken server-side from the catalog.</summary>
public record PlaceOrderItem(int CatalogItemId, int Quantity);

/// <summary>Optional shipping address for a placed order; a placeholder is used when omitted.</summary>
public record ShippingAddressInput(string Street, string City, string State, string Country, string ZipCode);

/// <summary>Pay an order either with a one-off card OR with one of the shopper's saved cards (exactly one).</summary>
public record PayCommand(GatewayCard? Card, int? SavedPaymentMethodId);

/// <summary>Save a card for the signed-in shopper.</summary>
public record SaveCardCommand(GatewayCard Card, string? Alias);

/// <summary>A single refund line in an order-payment view.</summary>
public record RefundView(string? RefundId, string Status, decimal Amount, DateTimeOffset CreatedAt);

/// <summary>The payment state of one order, returned to the caller.</summary>
public record OrderPaymentView(
    int OrderId,
    string Status,
    decimal Amount,
    string CurrencyCode,
    DateTimeOffset OrderDate,
    string? PayPalOrderId,
    string? AuthorizationId,
    DateTimeOffset? AuthorizationExpiresAt,
    string? CaptureId,
    decimal? CapturedAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    decimal TotalRefunded,
    IReadOnlyList<RefundView> Refunds);

/// <summary>The result of a refund request — <c>RefundId</c> is surfaced as a top-level field by the endpoint.</summary>
public record RefundResult(string? RefundId, string Status, decimal Amount, OrderPaymentView Order);

/// <summary>A saved card described safely enough for the shopper to recognise it — never full card details.</summary>
public record SavedCardView(
    int PaymentMethodId,
    string? Brand,
    string? Last4,
    string? Expiry,
    string? CardholderName,
    string? Alias,
    DateTimeOffset CreatedAt);

/// <summary>One PayPal transaction lined up (or not) against an eShop order.</summary>
public record ReconciliationEntry(
    string? TransactionId,
    string? InvoiceId,
    string? Status,
    decimal? Amount,
    string? CurrencyCode,
    decimal? Fee,
    DateTimeOffset? InitiationDate,
    int? MatchedOrderId);

/// <summary>An eShop captured payment PayPal's report did not show for the range.</summary>
public record UnmatchedOrderPayment(
    int OrderId,
    string? CaptureId,
    string? InvoiceId,
    decimal? CapturedAmount,
    string CurrencyCode,
    string Status);

/// <summary>
/// The reconciliation report over a date range: PayPal's transactions lined up against eShop orders,
/// so a payment PayPal knows about and eShop doesn't — or the reverse — is visible.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    IReadOnlyList<ReconciliationEntry> Matched,
    IReadOnlyList<ReconciliationEntry> InPayPalOnly,
    IReadOnlyList<UnmatchedOrderPayment> InEShopOnly);
