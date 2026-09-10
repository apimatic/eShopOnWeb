using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Payments;

/// <summary>Raw card details for a one-off payment or for vaulting. Never persisted by this app.</summary>
public record CardDetails(
    string Number,
    string Expiry,
    string SecurityCode,
    string? Name,
    CardBillingAddress? BillingAddress);

/// <summary>A billing address for a card. PayPal requires at least a country code.</summary>
public record CardBillingAddress(
    string? Line1,
    string? Line2,
    string? City,
    string? State,
    string? PostalCode,
    string CountryCode);

/// <summary>The outcome of authorizing a PayPal order: the ids and status of the hold.</summary>
public record AuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

/// <summary>The outcome of capturing an authorization, including what PayPal reported about the money.</summary>
public record CaptureResult(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal PayPalFee,
    decimal NetAmount);

/// <summary>The outcome of renewing a stale authorization.</summary>
public record ReauthorizationResult(
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

/// <summary>The outcome of a refund.</summary>
public record RefundResult(
    string RefundId,
    string Status,
    decimal Amount);

/// <summary>A card that has been vaulted at PayPal, described safely (no PAN).</summary>
public record VaultedCard(
    string VaultId,
    string? CustomerId,
    string? Brand,
    string? Last4,
    string? Expiry,
    string? CardHolderName);

/// <summary>One transaction as PayPal's reporting knows it, for reconciliation against eShop orders.</summary>
public record PayPalTransaction(
    string TransactionId,
    string? InvoiceId,
    string? CustomField,
    decimal Amount,
    string Currency,
    string Status,
    DateTimeOffset InitiationDate,
    string? EventCode);
