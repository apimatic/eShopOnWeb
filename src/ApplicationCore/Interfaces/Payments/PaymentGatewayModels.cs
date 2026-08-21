using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

/// <summary>A card's billing address, as PayPal expects it.</summary>
public record GatewayBillingAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea1, // state / province
    string? AdminArea2, // city
    string? PostalCode,
    string CountryCode);

/// <summary>
/// Raw card details for a one-off payment or to vault. These never touch the app's database and are
/// never logged — they flow straight through to PayPal.
/// </summary>
public record GatewayCard(
    string Number,
    string Expiry, // YYYY-MM
    string SecurityCode,
    string? CardholderName,
    GatewayBillingAddress? BillingAddress);

/// <summary>
/// How to pay: either a one-off raw card, or a previously vaulted card referenced by its token id.
/// Exactly one of the two is set.
/// </summary>
public record PaymentInstrument
{
    public GatewayCard? Card { get; private init; }
    public string? VaultId { get; private init; }

    public static PaymentInstrument FromCard(GatewayCard card) => new() { Card = card };
    public static PaymentInstrument FromVault(string vaultId) => new() { VaultId = vaultId };

    public bool IsVaulted => VaultId is not null;
}

/// <summary>Result of placing (and holding) a PayPal authorization for an order.</summary>
public record GatewayAuthorization(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

/// <summary>Current state of an authorization, read back from PayPal.</summary>
public record GatewayAuthorizationState(
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

/// <summary>Result of capturing an authorization — what PayPal actually reported.</summary>
public record GatewayCapture(
    string CaptureId,
    string Status,
    decimal CapturedAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    string CurrencyCode);

/// <summary>Result of a refund against a capture.</summary>
public record GatewayRefund(
    string RefundId,
    string Status,
    decimal Amount,
    string CurrencyCode);

/// <summary>A card saved into PayPal's vault, with a safe descriptor to show the shopper.</summary>
public record GatewayVaultedCard(
    string VaultId,
    string Brand,
    string LastDigits,
    string Expiry);

/// <summary>One transaction as PayPal's own reporting knows it, for reconciliation.</summary>
public record ReconciliationTransaction(
    string TransactionId,
    string? Status,
    decimal? Amount,
    string? CurrencyCode,
    decimal? FeeAmount,
    DateTimeOffset? Date);
