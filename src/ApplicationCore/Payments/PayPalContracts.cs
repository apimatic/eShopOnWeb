using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>A billing address for a card, in the shape PayPal expects (country code is required).</summary>
public sealed record PayPalBillingAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? State,
    string? PostalCode,
    string CountryCode);

/// <summary>Raw card details for a one-off payment or for vaulting. Never persisted or logged by this app.</summary>
public sealed record CardDetails(
    string Number,
    string Expiry,          // YYYY-MM
    string SecurityCode,
    string? CardholderName,
    PayPalBillingAddress? BillingAddress);

/// <summary>
/// The card to pay an order with: either a raw one-off card, or a previously vaulted card referenced by its
/// PayPal vault token id.
/// </summary>
public sealed class CardPaymentSource
{
    private CardPaymentSource(CardDetails? card, string? vaultId)
    {
        Card = card;
        VaultId = vaultId;
    }

    public CardDetails? Card { get; }
    public string? VaultId { get; }
    public bool IsVaulted => VaultId is not null;

    public static CardPaymentSource Raw(CardDetails card) => new(card, null);
    public static CardPaymentSource Vaulted(string vaultId) => new(null, vaultId);
}

/// <summary>The outcome of authorizing an order total (placing the hold).</summary>
public sealed record AuthorizationResult(
    string PayPalOrderId,
    string? AuthorizationId,
    string? AuthorizationStatus,
    string OrderStatus,
    bool RequiresBuyerApproval);

/// <summary>The outcome of capturing an authorization (taking the money) — what PayPal reported.</summary>
public sealed record CaptureResult(
    string CaptureId,
    string Status,
    decimal CapturedAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    string CurrencyCode);

/// <summary>The outcome of renewing a stale authorization.</summary>
public sealed record ReauthorizeResult(
    string AuthorizationId,
    string Status);

/// <summary>The outcome of voiding an authorization (releasing the hold).</summary>
public sealed record VoidResult(string Status);

/// <summary>The outcome of refunding a capture.</summary>
public sealed record RefundResult(
    string RefundId,
    string Status,
    decimal Amount);

/// <summary>A vaulted card: PayPal's token id plus a safe descriptor (never the PAN).</summary>
public sealed record VaultCardResult(
    string PaymentTokenId,
    string? CustomerId,
    string CardBrand,
    string LastFourDigits,
    string? Expiry,
    string? CardholderName);

/// <summary>One transaction as PayPal's own reporting records it, for reconciliation.</summary>
public sealed record PayPalTransactionRecord(
    string TransactionId,
    string? Status,
    decimal? Amount,
    string? CurrencyCode,
    string? InvoiceId,
    DateTimeOffset? InitiationDate);
