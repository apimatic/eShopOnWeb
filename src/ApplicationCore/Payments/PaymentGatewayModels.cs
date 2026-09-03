using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Raw card details for a one-off (unvaulted) card payment. These never touch the app's own database and
/// are never logged; they are handed straight to PayPal and then dropped.
/// </summary>
public record CardDetails(
    string? CardholderName,
    string Number,
    string Expiry,
    string? SecurityCode,
    string? BillingCountryCode = null,
    string? BillingPostalCode = null);

/// <summary>Authorize (place a hold) for an order total. Exactly one of <see cref="Card"/> or
/// <see cref="VaultTokenId"/> funds the payment.</summary>
public record AuthorizeRequest(
    int OrderId,
    decimal Amount,
    string CurrencyCode,
    string ReferenceId,
    CardDetails? Card,
    string? VaultTokenId,
    string IdempotencyKey);

public record AuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string? AuthorizationStatus);

public record CaptureCommand(
    string AuthorizationId,
    decimal Amount,
    string CurrencyCode,
    string IdempotencyKey);

public record CaptureResult(
    string CaptureId,
    string? CaptureStatus,
    decimal Gross,
    decimal? PayPalFee,
    decimal? NetAmount,
    string? RenewedAuthorizationId);

public record RefundCommand(
    string CaptureId,
    decimal? Amount,
    string CurrencyCode,
    string IdempotencyKey);

public record RefundResult(
    string RefundId,
    string? Status,
    decimal Amount);

public record VaultCardCommand(
    string PayPalCustomerId,
    CardDetails Card);

public record SavedCardResult(
    string VaultTokenId,
    string PayPalCustomerId,
    string? CardBrand,
    string? LastDigits,
    string? Expiry,
    string? CardholderName);

/// <summary>A single transaction as PayPal's own reporting records it, for reconciliation.</summary>
public record ReconciliationTransaction(
    string? TransactionId,
    string? Status,
    decimal? Amount,
    string? CurrencyCode,
    string? InvoiceId,
    string? CustomField,
    DateTimeOffset? InitiationDate);
