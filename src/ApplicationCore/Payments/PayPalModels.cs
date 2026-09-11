using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>Raw card details for a one-off payment or for vaulting. Never persisted or logged by this app.</summary>
public record CardDetails(
    string Number,
    string Expiry,
    string? SecurityCode,
    string? CardholderName,
    PayPalAddress? BillingAddress);

/// <summary>A PayPal portable postal address (subset of fields this app populates).</summary>
public record PayPalAddress(
    string CountryCode,
    string? AddressLine1 = null,
    string? AddressLine2 = null,
    string? AdminArea1 = null,
    string? AdminArea2 = null,
    string? PostalCode = null);

/// <summary>The funding source for an authorization: a raw card, or a previously vaulted card token.</summary>
public record PaymentSource
{
    public CardDetails? Card { get; init; }
    public string? VaultId { get; init; }

    public static PaymentSource FromCard(CardDetails card) => new() { Card = card };
    public static PaymentSource FromVault(string vaultId) => new() { VaultId = vaultId };
}

public record AuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

public record CaptureResult(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal PayPalFee,
    decimal NetAmount,
    string CurrencyCode);

public record ReauthorizeResult(
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

public record RefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    decimal TotalRefunded,
    string CurrencyCode);

public record VaultCardResult(
    string PaymentTokenId,
    string? CustomerId,
    string Brand,
    string Last4,
    string? Expiry,
    string? CardholderName);

/// <summary>A single row from PayPal's transaction reporting, normalised for reconciliation.</summary>
public record PayPalTransaction(
    string TransactionId,
    string? EventCode,
    string? Status,
    decimal Amount,
    decimal Fee,
    string CurrencyCode,
    DateTimeOffset? Date,
    string? InvoiceId,
    string? CustomField);
