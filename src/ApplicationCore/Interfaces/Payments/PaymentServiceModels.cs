using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

/// <summary>One line of a new order: a catalog item and how many.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

/// <summary>Optional shipping address for a placed order.</summary>
public record ShippingAddressInput(string Street, string City, string State, string Country, string ZipCode);

/// <summary>Raw card input from the shopper (never stored, never logged).</summary>
public record CardInput(
    string Number,
    string Expiry, // YYYY-MM
    string SecurityCode,
    string? CardholderName,
    string? BillingLine1,
    string? BillingLine2,
    string? BillingState,
    string? BillingCity,
    string? BillingPostalCode,
    string CountryCode);

/// <summary>Which instrument to pay with: a one-off card, or a saved card by its id. Exactly one is set.</summary>
public record PayInstruction(CardInput? Card, int? SavedPaymentMethodId);

/// <summary>Result of placing an order.</summary>
public record OrderPlaced(int OrderId, string Status, decimal Total, string Currency);

/// <summary>A refund as recorded against a payment.</summary>
public record RefundView(string RefundId, decimal Amount, string Status);

/// <summary>The payment state PayPal owns, projected for the API.</summary>
public record PaymentView(
    int OrderId,
    string OrderStatus,
    string PaymentStatus,
    decimal Amount,
    string Currency,
    string PayPalOrderId,
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

/// <summary>An order with its payment state, for the caller's order list.</summary>
public record OrderSummaryView(
    int OrderId,
    DateTimeOffset OrderDate,
    string OrderStatus,
    decimal Total,
    string Currency,
    PaymentView? Payment);

/// <summary>Result of a refund request (carries the refund id for the API's top-level field).</summary>
public record RefundResult(string RefundId, decimal Amount, string Status, string PaymentStatus, string OrderStatus);

/// <summary>A saved card, described safely.</summary>
public record SavedCardView(int PaymentMethodId, string Brand, string LastFourDigits, string Expiry);

/// <summary>How an eShop record and PayPal's reporting line up for one transaction.</summary>
public enum ReconciliationMatch
{
    Matched,
    PayPalOnly,
    EShopOnly
}

/// <summary>One reconciliation row.</summary>
public record ReconciliationRow(
    ReconciliationMatch Match,
    string? PayPalTransactionId,
    string? PayPalStatus,
    decimal? PayPalAmount,
    string? Currency,
    decimal? PayPalFee,
    DateTimeOffset? PayPalDate,
    int? OrderId,
    string? EShopKind,
    string? EShopReferenceId);

/// <summary>The reconciliation report over a date range.</summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    int MatchedCount,
    int PayPalOnlyCount,
    int EShopOnlyCount,
    IReadOnlyList<ReconciliationRow> Rows);
