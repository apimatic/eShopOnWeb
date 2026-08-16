using System;

namespace Microsoft.eShopWeb.ApplicationCore.PaymentGateway;

/// <summary>Raw card details for a one-off payment or for vaulting. Never persisted or logged.</summary>
public record PayPalRawCard(
    string Number,
    string Expiry,        // "YYYY-MM"
    string? SecurityCode,
    string? Name,
    PayPalBillingAddress? BillingAddress);

public record PayPalBillingAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea2,   // city
    string? AdminArea1,   // state / province
    string? PostalCode,
    string CountryCode);

/// <summary>
/// The card to charge: either a raw card (one-off) or a vaulted card referenced by its PayPal vault id.
/// Exactly one of the two is populated.
/// </summary>
public record PayPalCardInstrument(PayPalRawCard? RawCard, string? VaultId)
{
    public static PayPalCardInstrument FromRawCard(PayPalRawCard card) => new(card, null);
    public static PayPalCardInstrument FromVault(string vaultId) => new(null, vaultId);
    public bool IsVaulted => VaultId is not null;
}

public record PayPalAuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

public record PayPalCaptureResult(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal? PayPalFee,
    decimal? NetAmount);

public record PayPalRefundResult(
    string RefundId,
    string Status,
    decimal Amount);

public record PayPalVaultedCardResult(
    string VaultId,
    string? CustomerId,
    string Brand,
    string LastDigits,
    string? Expiry,
    string? CardholderName);

/// <summary>One row from PayPal's transaction reporting, trimmed to the fields reconciliation needs.</summary>
public record PayPalTransaction(
    string TransactionId,
    string? EventCode,
    string? Status,
    decimal? Amount,
    string? CurrencyCode,
    decimal? FeeAmount,
    DateTimeOffset? InitiationDate,
    string? InvoiceId,
    string? CustomField);
