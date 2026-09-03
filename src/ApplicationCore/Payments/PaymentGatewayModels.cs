using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Raw card details for a one-off payment or for vaulting. This type is passed to the payment
/// gateway and is never persisted in this application's database nor written to logs.
/// </summary>
public sealed record CardInput(
    string Number,
    string Expiry,          // "YYYY-MM"
    string SecurityCode,
    string? CardholderName,
    BillingAddressInput? BillingAddress);

/// <summary>Billing address for a card. Country code is required by PayPal.</summary>
public sealed record BillingAddressInput(
    string CountryCode,
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea1,     // state / province
    string? AdminArea2,     // city
    string? PostalCode);

/// <summary>
/// Everything the gateway needs to authorize (hold) an order total. Exactly one of
/// <see cref="Card"/> or <see cref="VaultId"/> is set.
/// </summary>
public sealed record AuthorizeInstruction(
    decimal Amount,
    string CurrencyCode,
    string InvoiceId,
    string? CustomId,
    string IdempotencyKey,
    CardInput? Card,
    string? VaultId);

/// <summary>Result of authorizing (holding) funds.</summary>
public sealed record AuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

/// <summary>Current state of an authorization, read back before fulfilment to detect staleness.</summary>
public sealed record AuthorizationSnapshot(
    string Status,
    DateTimeOffset? ExpiresAt);

/// <summary>Result of capturing an authorization, carrying what PayPal reported at capture.</summary>
public sealed record CaptureResult(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    string CurrencyCode);

/// <summary>Result of a refund.</summary>
public sealed record RefundResult(
    string RefundId,
    string Status,
    decimal Amount);

/// <summary>Descriptor of a vaulted card — safe to show the shopper, never the full number.</summary>
public sealed record VaultCardResult(
    string VaultId,
    string? Brand,
    string LastFourDigits,
    string? Expiry);

/// <summary>One transaction as PayPal's reporting knows it, for reconciliation.</summary>
public sealed record TransactionRecord(
    string TransactionId,
    string? InvoiceId,
    string? CustomField,
    decimal? Amount,
    string? CurrencyCode,
    string? Status,
    decimal? Fee,
    DateTimeOffset? Date,
    string? PaypalReferenceId);
