using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// A thin, verified client over the PayPal REST APIs this integration needs:
/// Orders v2 (create + authorize), Payments v2 (capture / reauthorize / void / refund),
/// Payment Method Tokens v3 (vault), and Transaction Search v1 (reconciliation).
/// Implementations target the PayPal sandbox or live environment per configuration.
/// </summary>
public interface IPayPalClient
{
    /// <summary>
    /// Creates a PayPal order for <paramref name="amount"/> and places a hold (authorization) on
    /// the money without capturing it. Exactly one of <paramref name="card"/> or
    /// <paramref name="vaultId"/> must be supplied. <paramref name="idempotencyKey"/> is sent as
    /// PayPal-Request-Id so a repeated call never authorizes twice.
    /// </summary>
    Task<PayPalAuthorization> AuthorizeAsync(decimal amount, string currency, string invoiceId,
        string orderReference, PayPalCard? card, string? vaultId, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Reads the current status/expiry of an authorization.</summary>
    Task<PayPalAuthorizationState> GetAuthorizationAsync(string authorizationId, CancellationToken ct = default);

    /// <summary>
    /// Captures (takes) the held money. <paramref name="idempotencyKey"/> is sent as
    /// PayPal-Request-Id so a repeated capture never charges twice. The result carries the
    /// captured amount, PayPal's fee and the net proceeds as PayPal reported them.
    /// </summary>
    Task<PayPalCapture> CaptureAsync(string authorizationId, decimal amount, string currency,
        string idempotencyKey, CancellationToken ct = default);

    /// <summary>Renews a stale authorization, yielding a fresh (possibly new) authorization id.</summary>
    Task<PayPalAuthorizationState> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        CancellationToken ct = default);

    /// <summary>Releases the held money (before capture).</summary>
    Task VoidAsync(string authorizationId, CancellationToken ct = default);

    /// <summary>
    /// Refunds a captured payment, in full (<paramref name="amount"/> null) or in part.
    /// <paramref name="idempotencyKey"/> is sent as PayPal-Request-Id so repeating the request
    /// under the same key never refunds twice.
    /// </summary>
    Task<PayPalRefundResult> RefundAsync(string captureId, decimal? amount, string currency,
        string idempotencyKey, CancellationToken ct = default);

    /// <summary>Saves a card into PayPal's vault and returns a reusable token plus a safe descriptor.</summary>
    Task<PayPalVaultedCard> VaultCardAsync(PayPalCard card, string? customerId, string idempotencyKey,
        CancellationToken ct = default);

    /// <summary>Removes a vaulted card so it can no longer be charged.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct = default);

    /// <summary>
    /// Lists PayPal's own transaction records across the whole [from, to] range (chunked to
    /// PayPal's per-request span limit and paged to completion).
    /// </summary>
    Task<IReadOnlyList<PayPalTransactionRecord>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct = default);
}
