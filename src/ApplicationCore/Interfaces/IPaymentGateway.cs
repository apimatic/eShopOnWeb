using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Raw card details supplied for a one-off payment or to be vaulted. These never touch the
/// application database or the logs — they flow straight through to the payment processor.
/// </summary>
public record CardDetails(
    string Number,
    string ExpiryMonth,
    string ExpiryYear,
    string SecurityCode,
    string? CardholderName = null,
    string? BillingLine1 = null,
    string? BillingCity = null,
    string? BillingState = null,
    string? BillingPostalCode = null,
    string? BillingCountryCode = null);

/// <summary>Result of placing a hold (authorization) on the shopper's money.</summary>
public record GatewayAuthorization(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

/// <summary>Result of taking the money (capturing an authorization).</summary>
public record GatewayCapture(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    string CurrencyCode);

/// <summary>Result of returning money (refunding a capture).</summary>
public record GatewayRefund(
    string RefundId,
    string Status,
    decimal Amount,
    string CurrencyCode);

/// <summary>Result of vaulting a card: the token that later stands in for it, plus a safe descriptor.</summary>
public record GatewayVaultedCard(
    string VaultId,
    string? Brand,
    string Last4,
    string? Expiry,
    string? CardholderName);

/// <summary>One transaction as PayPal's transaction report knows it, for reconciliation.</summary>
public record GatewayTransaction(
    string TransactionId,
    string Status,
    decimal Amount,
    string CurrencyCode,
    DateTimeOffset Date,
    string? EventCode);

/// <summary>
/// A payment processor. The single seam through which the application talks to PayPal;
/// implemented in Infrastructure over the PayPal SDK so that the domain never sees SDK types.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>Authorize (hold) <paramref name="amount"/> using raw card details. Browser-less.</summary>
    Task<GatewayAuthorization> AuthorizeWithCardAsync(decimal amount, string currencyCode, CardDetails card,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Authorize (hold) <paramref name="amount"/> using a previously vaulted card.</summary>
    Task<GatewayAuthorization> AuthorizeWithVaultedCardAsync(decimal amount, string currencyCode, string vaultId,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Capture a previously created authorization (take the money) at fulfilment.</summary>
    Task<GatewayCapture> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currencyCode,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Renew a stale authorization, returning a fresh authorization id.</summary>
    Task<GatewayAuthorization> ReauthorizeAsync(string authorizationId, decimal amount, string currencyCode,
        CancellationToken cancellationToken = default);

    /// <summary>Void an authorization before capture, releasing the held funds.</summary>
    Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Refund a capture in full (<paramref name="amount"/> null) or in part.</summary>
    Task<GatewayRefund> RefundCaptureAsync(string captureId, decimal? amount, string currencyCode,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Vault a card for later reuse.</summary>
    Task<GatewayVaultedCard> VaultCardAsync(CardDetails card, CancellationToken cancellationToken = default);

    /// <summary>Delete a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every transaction PayPal recorded in [from, to], paged through in full — not just the
    /// first page of the range.
    /// </summary>
    Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
