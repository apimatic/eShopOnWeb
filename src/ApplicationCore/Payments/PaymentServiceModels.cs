using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

// --- inputs ---

/// <summary>One catalog line on a placed order.</summary>
public record OrderLineInput(int CatalogItemId, int Quantity);

/// <summary>Shipping address supplied when placing an order.</summary>
public record ShippingAddressInput(string Street, string City, string State, string Country, string ZipCode);

/// <summary>How to fund an authorization: a one-off card, or one of the shopper's saved cards.</summary>
public record PaymentInstrument(CardDetails? Card, int? SavedPaymentMethodId);

// --- views ---

public record RefundSummary(string? RefundId, decimal Amount, string? Status, DateTimeOffset CreatedAt);

public record OrderPaymentSummary(
    int OrderId,
    DateTimeOffset OrderDate,
    decimal Total,
    string CurrencyCode,
    string PaymentStatus,
    string? PaymentMethodDescription,
    string? PayPalOrderId,
    string? AuthorizationId,
    string? AuthorizationStatus,
    string? CaptureId,
    string? CaptureStatus,
    decimal? CapturedGross,
    decimal? PayPalFee,
    decimal? NetAmount,
    decimal RefundedAmount,
    decimal RefundableAmount,
    IReadOnlyList<RefundSummary> Refunds);

public record SavedCardView(
    int PaymentMethodId,
    string? CardBrand,
    string? LastDigits,
    string? Expiry,
    string? CardholderName,
    DateTimeOffset CreatedAt);

public record ReconciliationLine(
    string MatchState,
    string? PayPalTransactionId,
    string? Status,
    decimal? Amount,
    string? CurrencyCode,
    string? InvoiceId,
    int? EShopOrderId,
    string? EShopPaymentStatus,
    DateTimeOffset? InitiationDate);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int MatchedCount,
    int PayPalOnlyCount,
    int EShopOnlyCount,
    IReadOnlyList<ReconciliationLine> Lines);
