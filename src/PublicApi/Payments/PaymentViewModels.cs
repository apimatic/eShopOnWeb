using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Payments;

// ---- Inputs (shopper-supplied) ----

public sealed record CardInput(
    string Number,
    string Expiry,
    string SecurityCode,
    string? CardholderName,
    BillingAddressInput? BillingAddress);

public sealed record BillingAddressInput(
    string CountryCode,
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea1,
    string? AdminArea2,
    string? PostalCode);

public sealed record OrderLineInput(int CatalogItemId, int Quantity);

public sealed record ShipToAddressInput(
    string? Street,
    string? City,
    string? State,
    string? Country,
    string? ZipCode);

// ---- Outputs ----

public sealed record PaymentView(
    int OrderId,
    string Status,
    decimal Amount,
    string CurrencyCode,
    string? PayPalOrderId,
    string? AuthorizationId,
    string? AuthorizationStatus,
    DateTimeOffset? AuthorizationExpiresAt,
    string? CaptureId,
    string? CaptureStatus,
    decimal? CapturedGross,
    decimal? PayPalFee,
    decimal? NetAmount,
    decimal RefundedAmount,
    string? InstrumentDescriptor,
    string? FailureReason);

public sealed record OrderLineView(int CatalogItemId, string ProductName, decimal UnitPrice, int Units);

public sealed record OrderPaymentView(
    int OrderId,
    DateTimeOffset OrderDate,
    decimal Total,
    IReadOnlyList<OrderLineView> Items,
    PaymentView Payment);

public sealed record RefundView(
    string RefundId,
    string Status,
    decimal Amount,
    string PaymentStatus,
    decimal TotalRefunded);

public sealed record SavedCardView(
    int PaymentMethodId,
    string? Brand,
    string? LastDigits,
    string? Expiry,
    string? CardholderName,
    string Descriptor,
    DateTimeOffset CreatedDate);

// ---- Reconciliation ----

public sealed record ReconciliationMatch(
    int OrderId,
    string EShopStatus,
    string? CaptureId,
    string? InvoiceId,
    decimal EShopAmount,
    string? PayPalTransactionId,
    decimal? PayPalAmount,
    string? PayPalStatus);

public sealed record ReconciliationPayPalOnly(
    string? TransactionId,
    string? InvoiceId,
    decimal? Amount,
    string? CurrencyCode,
    string? Status,
    string? InitiatedDate);

public sealed record ReconciliationEShopOnly(
    int OrderId,
    string Status,
    string? CaptureId,
    string InvoiceId,
    decimal Amount);

public sealed record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    bool CoveredAllPages,
    int PagesRead,
    int TotalPages,
    int PayPalTransactionCount,
    IReadOnlyList<ReconciliationMatch> Matched,
    IReadOnlyList<ReconciliationPayPalOnly> OnlyInPayPal,
    IReadOnlyList<ReconciliationEShopOnly> OnlyInEShop);
