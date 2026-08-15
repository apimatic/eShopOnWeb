using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>Raw card details for a one-off payment or for vaulting. Never persisted or logged.</summary>
/// <param name="Number">The card number (PAN).</param>
/// <param name="ExpiryYearMonth">Expiry in PayPal wire format, YYYY-MM.</param>
/// <param name="SecurityCode">The CVC/CVV.</param>
/// <param name="CardholderName">Name on the card, if provided.</param>
/// <param name="BillingLine1">Billing street, if provided.</param>
/// <param name="BillingCity">Billing city (admin_area_2), if provided.</param>
/// <param name="BillingState">Billing state/region (admin_area_1), if provided.</param>
/// <param name="BillingPostalCode">Billing postal code, if provided.</param>
/// <param name="BillingCountryCode">Two-letter billing country code.</param>
public record PayPalCardInput(
    string Number,
    string ExpiryYearMonth,
    string SecurityCode,
    string? CardholderName,
    string? BillingLine1,
    string? BillingCity,
    string? BillingState,
    string? BillingPostalCode,
    string BillingCountryCode);

/// <summary>Everything needed to authorize an order's total against PayPal.</summary>
/// <param name="Amount">The order total (from catalog prices) to hold, to the cent.</param>
/// <param name="ReferenceId">Our order id as a string; recorded on the PayPal purchase unit so the
/// reconciliation report can line the two ledgers up.</param>
/// <param name="RequestId">Idempotency key (PayPal-Request-Id) for create+authorize.</param>
/// <param name="Card">A one-off card, or null when paying with a saved card.</param>
/// <param name="VaultId">A saved card's vault token, or null when paying with a one-off card.</param>
public record PayPalAuthorizeCommand(
    decimal Amount,
    string ReferenceId,
    string RequestId,
    PayPalCardInput? Card,
    string? VaultId);

/// <summary>The outcome of authorizing (or reauthorizing) an order — the hold PayPal now owns.</summary>
public record PayPalAuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    string Currency,
    DateTimeOffset? ExpiresAt);

/// <summary>The outcome of capturing — what PayPal actually took and what the merchant nets.</summary>
public record PayPalCaptureResult(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    string Currency);

/// <summary>The outcome of a refund.</summary>
public record PayPalRefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency);

/// <summary>A vaulted card: the token to reuse plus a safe description of the card.</summary>
public record PayPalVaultedCardResult(
    string VaultId,
    string? CustomerId,
    string Brand,
    string LastFourDigits,
    string? ExpiryYearMonth,
    string? CardholderName);

/// <summary>One row from PayPal's transaction ledger, for reconciliation.</summary>
public record PayPalLedgerEntry(
    string TransactionId,
    string Status,
    decimal? Amount,
    string? Currency,
    DateTimeOffset? Date);
