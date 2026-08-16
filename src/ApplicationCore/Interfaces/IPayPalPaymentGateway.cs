using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The app's boundary to PayPal. Everything the domain needs to move money is expressed here
/// in the app's own terms; the implementation (in Infrastructure) is the only place that knows
/// the PayPal SDK exists. All amounts are in the currency configured for the integration.
/// </summary>
public interface IPayPalPaymentGateway
{
    /// <summary>
    /// Places a hold for <paramref name="amount"/> using the given card (a one-off raw card or a
    /// saved/vaulted card). Money is authorized, not taken. The idempotency key makes a
    /// double-submit safe.
    /// </summary>
    Task<PayPalAuthorizationResult> AuthorizeAsync(PayPalPaymentInstrument instrument, decimal amount,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Captures (takes the money for) a previously authorized payment.</summary>
    Task<PayPalCaptureResult> CaptureAsync(string authorizationId, string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>Renews a stale authorization so it can still be captured.</summary>
    Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Releases a hold before capture, so no money moves.</summary>
    Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refunds a capture, fully (<paramref name="amount"/> null) or partially. The idempotency key
    /// is the caller-supplied one, so a replay never refunds twice.
    /// </summary>
    Task<PayPalRefundResult> RefundAsync(string captureId, decimal? amount, string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>Vaults a raw card and returns a token plus a safe description of the card.</summary>
    Task<PayPalVaultedCardResult> VaultCardAsync(PayPalCardDetails card, CancellationToken cancellationToken = default);

    /// <summary>Removes a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists PayPal's own record of transactions across the whole date range (all pages), for
    /// reconciliation against eShop orders.
    /// </summary>
    Task<IReadOnlyList<PayPalTransactionRecord>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}

/// <summary>Raw card details for a one-off payment or for vaulting. Never persisted or logged.</summary>
public sealed record PayPalCardDetails(
    string Number,
    string Expiry,          // "YYYY-MM"
    string SecurityCode,
    string? CardholderName,
    string? BillingAddressLine1 = null,
    string? BillingCity = null,
    string? BillingState = null,
    string? BillingCountryCode = null,
    string? BillingPostalCode = null);

/// <summary>How an order is being paid: either a one-off raw card, or a saved (vaulted) card.</summary>
public sealed class PayPalPaymentInstrument
{
    public PayPalCardDetails? Card { get; init; }
    public string? VaultTokenId { get; init; }

    public static PayPalPaymentInstrument FromCard(PayPalCardDetails card) => new() { Card = card };
    public static PayPalPaymentInstrument FromVaultToken(string vaultTokenId) => new() { VaultTokenId = vaultTokenId };
}

public sealed record PayPalAuthorizationResult(string? PayPalOrderId, string AuthorizationId, string Status, DateTimeOffset? ExpiresAt);

public sealed record PayPalCaptureResult(string CaptureId, string Status, decimal GrossAmount, decimal? PayPalFee, decimal? NetAmount);

public sealed record PayPalRefundResult(string RefundId, string Status, decimal Amount);

public sealed record PayPalVaultedCardResult(string TokenId, string Brand, string LastFourDigits, string? CardholderName, string? Expiry);

public sealed record PayPalTransactionRecord(string TransactionId, string Status, decimal? Amount, string? CurrencyCode, DateTimeOffset? InitiatedAt);
