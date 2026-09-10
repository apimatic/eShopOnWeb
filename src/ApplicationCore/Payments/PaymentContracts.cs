using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Raw card details supplied for a one-off payment or to vault a card. These are transient: they are
/// passed to the payment processor and never stored in this application's database nor written to logs.
/// </summary>
public record CardDetails(string Number, string Expiry, string SecurityCode, string? CardholderName);

/// <summary>A billing address that can accompany a card.</summary>
public record BillingAddress(string? AddressLine1, string? AdminArea2, string? AdminArea1,
    string? PostalCode, string? CountryCode);

/// <summary>How a payment is funded: either raw card details, or a previously saved card's vault token.</summary>
public record PaymentInstrument(CardDetails? Card, string? VaultToken, BillingAddress? BillingAddress);

/// <summary>A request to authorize (hold) an order total.</summary>
/// <param name="ReconciliationReference">Stable eShop ref echoed to PayPal as custom_id (for reconciliation).</param>
/// <param name="PaymentReference">Globally-unique ref used as PayPal's invoice id and idempotency-key stem.</param>
public record AuthorizationRequest(
    int OrderId,
    decimal Amount,
    string CurrencyCode,
    string ReconciliationReference,
    string PaymentReference,
    PaymentInstrument Instrument);

/// <summary>The outcome of an authorization attempt.</summary>
public enum AuthorizationOutcome
{
    Authorized,
    Declined,
    Pending,
    /// <summary>PayPal requires the shopper to approve in a browser (e.g. 3DS). Not supported here.</summary>
    ChallengeRequired
}

public record AuthorizationResult(
    AuthorizationOutcome Outcome,
    string? ProcessorOrderId,
    string? AuthorizationId,
    string RawStatus,
    decimal Amount,
    string CurrencyCode,
    DateTimeOffset? ExpiresAt,
    string? PaymentMethodDescription,
    string? DeclineReason);

public record CaptureResult(
    string CaptureId,
    string RawStatus,
    decimal GrossAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    string CurrencyCode,
    bool Completed);

public record ReauthorizeResult(
    bool Renewed,
    string? AuthorizationId,
    string RawStatus,
    DateTimeOffset? ExpiresAt,
    string? Reason);

public record RefundResult(string RefundId, string RawStatus, decimal Amount);

/// <summary>A request to vault a card for a shopper.</summary>
public record VaultCardRequest(string BuyerId, string? PayPalCustomerId, CardDetails Card, BillingAddress? BillingAddress);

public record SavedCardResult(
    string VaultToken,
    string? PayPalCustomerId,
    string Brand,
    string LastDigits,
    string? Expiry,
    string? CardholderName);

/// <summary>PayPal's own record of a transaction, used to reconcile against eShop orders.</summary>
public record ReconciliationTransaction(
    string TransactionId,
    string? Status,
    decimal? Amount,
    string? CurrencyCode,
    string? InvoiceId,
    string? CustomField,
    DateTimeOffset? InitiatedAt);
