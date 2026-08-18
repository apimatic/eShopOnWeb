using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Raw card details for a one-off payment or for vaulting. These flow through as method arguments
/// only; they are never persisted in this app's database and never written to logs.
/// </summary>
public record CardDetails(
    string Number,
    string Expiry, // YYYY-MM
    string SecurityCode,
    string? CardholderName,
    PaymentCardBillingAddress BillingAddress);

/// <summary>Card billing address. Field names mirror PayPal's address model.</summary>
public record PaymentCardBillingAddress(
    string CountryCode,
    string? AddressLine1 = null,
    string? AddressLine2 = null,
    string? AdminArea1 = null,  // state / province
    string? AdminArea2 = null,  // city
    string? PostalCode = null);

/// <summary>
/// How to pay: either raw card details (one-off) or a saved-card vault id. Exactly one is set.
/// </summary>
public sealed class PayPalPaymentSource
{
    public CardDetails? Card { get; }
    public string? VaultId { get; }

    private PayPalPaymentSource(CardDetails? card, string? vaultId)
    {
        Card = card;
        VaultId = vaultId;
    }

    public static PayPalPaymentSource FromCard(CardDetails card) => new(card, null);
    public static PayPalPaymentSource FromVault(string vaultId) => new(null, vaultId);
}

/// <summary>Result of authorizing an order total (funds held, not captured).</summary>
public record PayPalAuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string? Status,
    DateTimeOffset? ExpiresAt);

/// <summary>Result of capturing an authorization (money taken), including PayPal's fee and net proceeds.</summary>
public record PayPalCaptureResult(
    string CaptureId,
    string? Status,
    decimal CapturedAmount,
    decimal? Fee,
    decimal? NetAmount,
    string CurrencyCode);

/// <summary>Result of re-authorizing a stale hold.</summary>
public record PayPalReauthorizationResult(
    string AuthorizationId,
    string? Status,
    DateTimeOffset? ExpiresAt);

/// <summary>Result of refunding a captured payment (full or partial).</summary>
public record PayPalRefundResult(
    string RefundId,
    string? Status,
    decimal Amount);

/// <summary>Safe descriptor of a vaulted card — never the full PAN.</summary>
public record PayPalVaultedCard(
    string VaultId,
    string? Brand,
    string? LastDigits,
    string? Expiry,
    string? CardholderName);

/// <summary>One transaction as PayPal's reporting knows it, for reconciliation.</summary>
public record PayPalTransactionRecord(
    string TransactionId,
    decimal? Amount,
    string? CurrencyCode,
    string? Status,
    DateTimeOffset? Date);
