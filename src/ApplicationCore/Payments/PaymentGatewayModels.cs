using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>Full card details for a one-off payment or for vaulting. Never stored or logged.</summary>
public record CardDetails(
    string Number,
    string Expiry,          // "YYYY-MM"
    string? SecurityCode,
    string? CardholderName,
    BillingAddress? BillingAddress);

/// <summary>Billing address supplied with a card (used for AVS / vaulting).</summary>
public record BillingAddress(
    string? Line1,
    string? Line2,
    string? City,
    string? State,
    string? PostalCode,
    string? CountryCode);

/// <summary>What is being paid, and with which instrument (raw card or saved vault token).</summary>
public record PaymentAuthorizationRequest(
    decimal Amount,
    string Currency,
    string InvoiceId,
    string CustomId,
    string IdempotencyKey,
    CardDetails? Card,
    string? VaultId);

/// <summary>Outcome of placing (or renewing) a hold on the money.</summary>
public record AuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt,
    bool RequiresBrowserApproval);

/// <summary>Current PayPal state of an authorization, used to decide whether it is still usable.</summary>
public record AuthorizationSnapshot(
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

/// <summary>Outcome of capturing (taking) the money, as PayPal reported it.</summary>
public record CaptureResult(
    string CaptureId,
    string Status,
    decimal CapturedAmount,
    decimal? PayPalFee,
    decimal? NetAmount);

/// <summary>Outcome of refunding a capture, in full or in part.</summary>
public record RefundResult(
    string RefundId,
    string Status,
    decimal Amount);

/// <summary>Outcome of vaulting a card for later reuse. Carries only a safe descriptor.</summary>
public record VaultCardResult(
    string VaultId,
    string? CustomerId,
    string Brand,
    string LastFourDigits,
    string Expiry,
    bool RequiresBrowserApproval);

/// <summary>One transaction as PayPal's own records report it, for reconciliation.</summary>
public record GatewayTransaction(
    string TransactionId,
    string Status,
    decimal Amount,
    string Currency,
    string? InvoiceId,
    string? CustomField,
    DateTimeOffset? InitiatedAt);
