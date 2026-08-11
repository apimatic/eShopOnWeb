using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>Result of creating a card authorization (the hold) via a PayPal checkout order.</summary>
public record CardAuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string AuthorizationStatus,
    DateTimeOffset? ExpiresAt,
    string? CardBrand,
    string? CardLast4,
    string? CardExpiry);

/// <summary>Result of capturing an authorization at fulfilment.</summary>
public record CaptureResult(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    string Currency);

/// <summary>Result of reauthorizing a stale authorization (a new authorization).</summary>
public record ReauthorizationResult(
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

/// <summary>Result of refunding a capture.</summary>
public record RefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency);

/// <summary>A card saved to PayPal's vault, described safely for the shopper.</summary>
public record VaultedCard(
    string VaultId,
    string? CustomerId,
    string? Brand,
    string? Last4,
    string? Expiry,
    string? CardholderName);

/// <summary>One transaction as PayPal's reporting API knows it (for reconciliation).</summary>
public record PayPalTransaction(
    string TransactionId,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset? InitiatedAt,
    string? EventCode);
