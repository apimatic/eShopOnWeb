using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Domain-facing DTOs for the PayPal gateway port. These deliberately carry no SDK types, so the
/// application layer never depends on the PayPal SDK. Full card numbers/CVV live only on the request
/// DTOs long enough to reach PayPal; they are never persisted or logged.
/// </summary>

/// <summary>A one-off card supplied by the shopper for a single payment or to be vaulted.</summary>
public record CardDetails(
    string Number,
    string Expiry,          // ISO-8601 YYYY-MM
    string SecurityCode,
    string? CardHolderName,
    CardBillingAddress? BillingAddress);

/// <summary>Optional billing address for a card (improves acceptance rates; PayPal accepts card-only too).</summary>
public record CardBillingAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea1,     // state / province
    string? AdminArea2,     // city
    string? PostalCode,
    string? CountryCode);   // ISO-3166-1 alpha-2

/// <summary>
/// Request to authorize (hold) an order total. Exactly one funding source is set: a one-off
/// <see cref="Card"/>, or a <see cref="VaultId"/> naming one of the shopper's saved cards.
/// </summary>
public record AuthorizeRequest
{
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
    /// <summary>Reconciliation key echoed to PayPal as the purchase-unit custom_id.</summary>
    public required string CustomId { get; init; }
    /// <summary>Unique-per-order invoice id echoed to PayPal as the purchase-unit invoice_id.</summary>
    public required string InvoiceId { get; init; }
    /// <summary>Deterministic seed used to derive idempotency (PayPal-Request-Id) keys for the create/authorize calls.</summary>
    public required string RequestIdSeed { get; init; }
    public CardDetails? Card { get; init; }
    public string? VaultId { get; init; }
    /// <summary>Short human description shown on the PayPal record.</summary>
    public string? Description { get; init; }
}

/// <summary>The hold PayPal placed: the ids and status a later capture/void/reauthorize will act on.</summary>
public record AuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    decimal AuthorizedAmount,
    string Currency,
    DateTimeOffset? ExpiresAt);

/// <summary>The money actually taken at capture, with PayPal's reported fee and net proceeds.</summary>
public record CaptureResult(
    string CaptureId,
    string Status,
    decimal CapturedAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    string Currency);

/// <summary>A renewed hold after the original expired before fulfilment.</summary>
public record ReauthorizeResult(
    string AuthorizationId,
    string Status,
    decimal AuthorizedAmount,
    string Currency,
    DateTimeOffset? ExpiresAt);

/// <summary>A refund PayPal recorded against a capture.</summary>
public record RefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency);

/// <summary>Request to vault (save) a card for reuse. The customer id ties the token to the shopper at PayPal.</summary>
public record VaultCardRequest(CardDetails Card, string MerchantCustomerId);

/// <summary>The saved card: the vault id used to charge it later, plus a safe descriptor for display.</summary>
public record VaultedCardResult(
    string VaultId,
    string? Brand,
    string? LastFourDigits,
    string? Expiry,
    string? CardHolderName,
    string? CustomerId);

/// <summary>One transaction from PayPal's own record, for reconciliation against eShop orders.</summary>
public record PayPalTransaction(
    string? TransactionId,
    string? Status,
    decimal? Amount,
    decimal? FeeAmount,
    string? Currency,
    string? InvoiceId,
    string? CustomId,
    DateTimeOffset? InitiatedAt,
    string? EventCode);
