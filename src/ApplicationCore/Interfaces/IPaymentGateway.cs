using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Raw card details for a one-off payment or to vault a card. These never leave the request
/// pipeline into this app's own storage or logs — the gateway forwards them straight to PayPal.
/// </summary>
public record CardDetails(
    string Number,
    string Expiry,
    string SecurityCode,
    string? CardholderName = null,
    string? BillingLine1 = null,
    string? BillingCity = null,
    string? BillingState = null,
    string? BillingCountryCode = null,
    string? BillingPostalCode = null);

/// <summary>Result of authorizing an order total (a hold on the funds).</summary>
public record AuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

/// <summary>Result of capturing an authorization (the money actually taken), with PayPal's own figures.</summary>
public record CaptureResult(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    string CurrencyCode);

/// <summary>Result of refunding a captured payment, in full or in part.</summary>
public record RefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string CurrencyCode);

/// <summary>Current state of an authorization, used to decide whether it must be renewed before capture.</summary>
public record AuthorizationState(string Status, DateTimeOffset? ExpiresAt);

/// <summary>A vaulted card: PayPal's token id plus a safe description (never the full number).</summary>
public record VaultedCard(
    string TokenId,
    string? CustomerId,
    string? Brand,
    string? LastFourDigits,
    string? Expiry);

/// <summary>One transaction as PayPal's own reporting knows it, for reconciliation.</summary>
public record PayPalTransaction(
    string TransactionId,
    string? Status,
    decimal? Amount,
    string? CurrencyCode,
    DateTimeOffset? Date);

/// <summary>
/// The boundary between this app and PayPal. Every PayPal interaction goes through here; the
/// implementation lives in Infrastructure and maps PayPal's SDK types onto these domain types,
/// so no SDK type leaks into ApplicationCore. Failures surface as
/// <see cref="Microsoft.eShopWeb.ApplicationCore.Exceptions.PaymentGatewayException"/>.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>
    /// Authorize (hold, not capture) <paramref name="amount"/> in <paramref name="currencyCode"/>,
    /// paying either with raw <paramref name="card"/> details or a saved-card <paramref name="vaultId"/>.
    /// <paramref name="idempotencyKey"/> makes a double-submit safe. Throws
    /// <see cref="Microsoft.eShopWeb.ApplicationCore.Exceptions.PaymentChallengeRequiredException"/>
    /// if PayPal requires browser approval.
    /// </summary>
    Task<AuthorizationResult> AuthorizeAsync(decimal amount, string currencyCode, CardDetails? card,
        string? vaultId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Capture a previously authorized payment (take the money) at fulfilment.</summary>
    Task<CaptureResult> CaptureAsync(string authorizationId, string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>Inspect an authorization's current status/expiry.</summary>
    Task<AuthorizationState> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken = default);

    /// <summary>Renew a stale authorization, yielding a fresh authorization to capture.</summary>
    Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>Void an authorization before fulfilment, releasing the held funds.</summary>
    Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refund a captured payment. A null <paramref name="amount"/> refunds the full remaining amount;
    /// a value refunds that part. <paramref name="idempotencyKey"/> is the caller-supplied key.
    /// </summary>
    Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currencyCode,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Vault (save) a card for reuse. <paramref name="existingCustomerId"/> reuses a shopper's PayPal
    /// customer id when they already have one; <paramref name="merchantCustomerId"/> is our stable id
    /// for the shopper when they do not.
    /// </summary>
    Task<VaultedCard> VaultCardAsync(CardDetails card, string? existingCustomerId, string merchantCustomerId,
        CancellationToken cancellationToken = default);

    /// <summary>Remove a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string tokenId, CancellationToken cancellationToken = default);

    /// <summary>
    /// All transactions PayPal's reporting holds for the date range, across every page. Used to
    /// reconcile against eShop's own records.
    /// </summary>
    Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
