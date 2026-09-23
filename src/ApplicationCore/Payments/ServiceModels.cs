using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public record OrderLineInput(int CatalogItemId, int Quantity);

public record AddressInput(string Street, string City, string State, string Country, string ZipCode);

/// <summary>Funding for a pay request: exactly one of a one-off card or a saved payment-method id.</summary>
public record PayInput(CardDetails? Card, int? SavedPaymentMethodId);

public record RefundSummary(string? RefundId, string? Status, decimal Amount);

/// <summary>The caller-facing view of an order's payment state.</summary>
public record PaymentView(
    int OrderId,
    string Status,
    string Currency,
    decimal Amount,
    string InvoiceId,
    string? PayPalOrderId,
    string? AuthorizationId,
    string? AuthorizationStatus,
    DateTimeOffset? AuthorizationExpiresAt,
    string? CaptureId,
    string? CaptureStatus,
    decimal? CapturedGross,
    decimal? PaypalFee,
    decimal? NetAmount,
    decimal RefundedAmount,
    IReadOnlyList<RefundSummary> Refunds);

public record RefundView(string RefundId, string Status, decimal Amount, string Currency);

public record SavedCardView(int PaymentMethodId, string? Brand, string? LastDigits, string? Expiry, string? CardholderName);

// ── Reconciliation ───────────────────────────────────────────────────────
public record ReconciledLine(
    int OrderId,
    string InvoiceId,
    string EShopStatus,
    decimal EShopAmount,
    string? PayPalTransactionId,
    decimal? PayPalAmount,
    string? PayPalStatus);

public record PayPalOnlyLine(string? TransactionId, string? InvoiceId, decimal? Amount, string? Currency, string? Status);

public record EShopOnlyLine(int OrderId, string InvoiceId, string EShopStatus, decimal EShopAmount);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciledLine> Matched,
    IReadOnlyList<PayPalOnlyLine> OnlyInPayPal,
    IReadOnlyList<EShopOnlyLine> OnlyInEShop,
    int PayPalPagesFetched,
    int PayPalTotalPages,
    bool Truncated);
