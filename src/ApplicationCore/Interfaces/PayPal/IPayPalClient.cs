using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

/// <summary>
/// Talks to PayPal's REST API. This is the single seam through which the app performs every PayPal
/// interaction (authorize, capture, void, reauthorize, refund, vault, reporting).
/// </summary>
public interface IPayPalClient
{
    /// <summary>Creates an order with intent=AUTHORIZE and holds the funds (no capture yet).</summary>
    Task<PayPalAuthorizationResult> AuthorizeOrderAsync(PayPalAuthorizeRequest request, CancellationToken ct = default);

    /// <summary>Reads the current status of an authorization (e.g. CREATED, CAPTURED, EXPIRED, VOIDED).</summary>
    Task<string?> GetAuthorizationStatusAsync(string authorizationId, CancellationToken ct = default);

    /// <summary>Captures (takes) the money for a held authorization; returns PayPal's fee/net breakdown.</summary>
    Task<PayPalCaptureResult> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currency,
        string requestId, CancellationToken ct = default);

    /// <summary>Renews a stale authorization (allowed by PayPal only within its reauthorization window).</summary>
    Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        string requestId, CancellationToken ct = default);

    /// <summary>Voids a held authorization, releasing the shopper's funds.</summary>
    Task VoidAuthorizationAsync(string authorizationId, string requestId, CancellationToken ct = default);

    /// <summary>Refunds a capture in full or in part; idempotent on <paramref name="requestId"/>.</summary>
    Task<PayPalRefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currency,
        string requestId, string? noteToPayer, CancellationToken ct = default);

    /// <summary>Vaults a card (no charge) and returns a safe descriptor + the vault/customer ids.</summary>
    Task<PayPalVaultCardResult> VaultCardAsync(PayPalCardDetails card, string? customerId, string requestId,
        CancellationToken ct = default);

    /// <summary>Removes a vaulted card so it can no longer be charged.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct = default);

    /// <summary>
    /// Lists every PayPal transaction across the whole range, transparently paging and splitting the
    /// range into windows within PayPal's 31-day reporting limit.
    /// </summary>
    Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct = default);
}
