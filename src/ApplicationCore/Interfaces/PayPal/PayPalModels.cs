using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

/// <summary>Billing address for a one-off card, mapped to PayPal's portable address.</summary>
public record BillingAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea2,
    string? AdminArea1,
    string? PostalCode,
    string CountryCode);

/// <summary>Raw card details for a one-off payment or for vaulting. These are passed
/// straight to PayPal and are never persisted or logged by this application.</summary>
public record CardDetails(
    string Number,
    string Expiry,          // YYYY-MM
    string? SecurityCode,
    string? Name,
    BillingAddress? Billing);

/// <summary>How the shopper is paying: a one-off card, or one of their vaulted cards.</summary>
public record PaymentSourceInstruction
{
    public CardDetails? Card { get; init; }
    public string? VaultId { get; init; }

    public static PaymentSourceInstruction FromCard(CardDetails card) => new() { Card = card };
    public static PaymentSourceInstruction FromVault(string vaultId) => new() { VaultId = vaultId };
}

public record AuthorizeInstruction(
    string PayPalRequestId,
    decimal Amount,
    string CurrencyCode,
    string OrderReference,
    PaymentSourceInstruction Source);

public enum AuthorizeStatus
{
    Authorized,
    ChallengeRequired,
    Failed
}

public record AuthorizeResult(
    AuthorizeStatus Status,
    string? PayPalOrderId,
    string? AuthorizationId,
    string? AuthorizationStatus,
    DateTimeOffset? ExpiresAt,
    string? FailureReason);

public record AuthorizationInfo(
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

public record CaptureResult(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    string CurrencyCode);

public record RefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string CurrencyCode);

public record VaultCardResult(
    string VaultId,
    string CustomerId,
    string? Brand,
    string? LastDigits,
    string? Expiry,
    string? Name);

/// <summary>One transaction as PayPal's own reporting knows it, used for reconciliation.</summary>
public record PayPalTransaction(
    string TransactionId,
    string? ReferenceId,
    string? EventCode,
    string? Status,
    decimal? Amount,
    string? CurrencyCode,
    decimal? FeeAmount,
    DateTimeOffset? InitiationDate);
