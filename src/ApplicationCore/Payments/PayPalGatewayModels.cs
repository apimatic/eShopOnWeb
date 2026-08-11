using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Billing address for a card, mirroring the fields PayPal accepts. Country code is required.
/// </summary>
public record CardBillingAddress(
    string CountryCode,
    string? AddressLine1 = null,
    string? AddressLine2 = null,
    string? AdminArea2 = null,   // city
    string? AdminArea1 = null,   // state / province
    string? PostalCode = null);

/// <summary>
/// Raw card details for a one-off payment or for vaulting. These are passed straight to PayPal and
/// are never persisted or logged by this application.
/// </summary>
public record RawCard(
    string Number,
    string Expiry,          // "YYYY-MM"
    string SecurityCode,
    string? Name = null,
    CardBillingAddress? BillingAddress = null);

/// <summary>Result of placing a hold (authorization) on the shopper's money.</summary>
public record PayPalAuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string AuthorizationStatus,
    DateTimeOffset? ExpiresAt);

/// <summary>Result of capturing an authorization at fulfilment.</summary>
public record PayPalCaptureResult(
    string CaptureId,
    string Status,
    decimal Gross,
    decimal? Fee,
    decimal? Net,
    string Currency);

/// <summary>Result of renewing a stale authorization.</summary>
public record PayPalReauthorizationResult(
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

/// <summary>Result of refunding a captured payment (full or partial).</summary>
public record PayPalRefundResult(
    string RefundId,
    string Status,
    decimal Amount);

/// <summary>Result of vaulting (saving) a card at PayPal.</summary>
public record PayPalVaultResult(
    string TokenId,
    string Brand,
    string Last4,
    string? Expiry);

/// <summary>One transaction as PayPal's reporting records it, for reconciliation.</summary>
public record PayPalTransactionRecord(
    string TransactionId,
    decimal? Amount,
    string? Currency,
    string? Status,
    DateTimeOffset? Date,
    string? InvoiceId,
    string? CustomField);
