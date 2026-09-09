using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>Raw card details for a one-off payment or for vaulting. Never persisted by this app.</summary>
public record CardDetails(
    string Number,
    string Expiry, // YYYY-MM
    string SecurityCode,
    string? Name,
    CardBillingAddress? BillingAddress);

/// <summary>Portable billing address for a card. CountryCode is required by PayPal.</summary>
public record CardBillingAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea2, // city
    string? AdminArea1, // state
    string? PostalCode,
    string CountryCode);

/// <summary>
/// A request to create an authorized order. Exactly one of <see cref="Card"/> or
/// <see cref="VaultTokenId"/> funds the payment.
/// </summary>
public record CreateAuthorizationRequest(
    decimal Amount,
    string Currency,
    string InvoiceId,
    string RequestId,
    CardDetails? Card,
    string? VaultTokenId,
    string? Description);

/// <summary>The outcome of creating an authorized order: the ids and status the gateway now owns.</summary>
public record AuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

/// <summary>Current status/expiry of a hold.</summary>
public record AuthorizationDetails(
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

/// <summary>The outcome of a capture, including PayPal's fee and net proceeds.</summary>
public record CaptureResult(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    string Currency);

/// <summary>The outcome of a refund.</summary>
public record RefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency);

/// <summary>A vaulted card: its durable token and safe-to-show details.</summary>
public record VaultedCardResult(
    string VaultTokenId,
    string? Brand,
    string? LastDigits,
    string? Expiry);

/// <summary>One transaction from PayPal's transaction reporting, for reconciliation.</summary>
public record ReconciliationTransaction(
    string TransactionId,
    string? InvoiceId,
    decimal? Amount,
    string? Currency,
    string? Status,
    DateTimeOffset? Date,
    string? EventCode);
