using System;

namespace Microsoft.eShopWeb.ApplicationCore.PayPal;

/// <summary>Billing address for a card (all optional; PayPal's sandbox test card needs none).</summary>
public record BillingAddressInput(
    string? AddressLine1 = null,
    string? AddressLine2 = null,
    string? AdminArea1 = null,
    string? AdminArea2 = null,
    string? PostalCode = null,
    string? CountryCode = null);

/// <summary>Raw one-off card details supplied by the caller. Never persisted, never logged.</summary>
public record CardDetails(
    string Number,
    string Expiry,          // YYYY-MM
    string? SecurityCode = null,
    string? Name = null,
    BillingAddressInput? BillingAddress = null);

/// <summary>
/// How a payment is funded: either a one-off <see cref="Card"/> or a saved card's
/// <see cref="VaultId"/>. Exactly one is set.
/// </summary>
public record CardPaymentInstrument
{
    public CardDetails? Card { get; init; }
    public string? VaultId { get; init; }

    public static CardPaymentInstrument OneOff(CardDetails card) => new() { Card = card };
    public static CardPaymentInstrument Saved(string vaultId) => new() { VaultId = vaultId };
}

/// <summary>Result of placing a hold on the money (authorization).</summary>
public record AuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    decimal Amount,
    string Currency);

/// <summary>Result of taking the money (capture), carrying what PayPal reported.</summary>
public record CaptureResult(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal? PaypalFee,
    decimal? NetAmount,
    string Currency);

/// <summary>Result of ensuring an authorization is still capturable (renewing a stale hold).</summary>
public record AuthorizationRenewalResult(
    bool Renewed,
    string AuthorizationId,
    bool CanCapture,
    string? Reason);

/// <summary>Result of a refund.</summary>
public record RefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency);

/// <summary>A card saved (vaulted) at PayPal, described safely for the shopper.</summary>
public record SavedCardResult(
    string VaultId,
    string CustomerId,
    string? Brand,
    string? LastFourDigits,
    string? Expiry);

/// <summary>One PayPal-side transaction as reported by transaction search, for reconciliation.</summary>
public record PayPalTransactionRecord(
    string? TransactionId,
    string? CorrelationId,   // the merchant custom text (custom_id), falling back to invoice_id
    decimal? Amount,
    string? Currency,
    string? Status,
    DateTimeOffset? InitiationDate);
