using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Domain-facing abstraction over PayPal. The concrete implementation (in Infrastructure) is the only code
/// that touches the PayPal SDK; everything above this interface works in eShop's own terms. This keeps
/// ApplicationCore free of any SDK type and makes the payment flow testable without a network.
/// </summary>
public interface IPayPalGateway
{
    /// <summary>The configured settlement currency (from <c>PayPal:Currency</c>).</summary>
    string Currency { get; }

    /// <summary>
    /// Create a PayPal order with intent AUTHORIZE for <paramref name="amount"/> and place a hold on the
    /// money (authorize) — the funds are held, not taken. Pays with a one-off card or a vaulted card.
    /// <paramref name="idempotencyKeyPrefix"/> makes the create+authorize pair safe to repeat.
    /// </summary>
    Task<AuthorizationResult> AuthorizeAsync(decimal amount, CardPaymentInstrument instrument,
        string idempotencyKeyPrefix, CancellationToken ct);

    /// <summary>Read the current state of an authorization (used to detect a stale hold before capture).</summary>
    Task<AuthorizationDetails> GetAuthorizationAsync(string authorizationId, CancellationToken ct);

    /// <summary>Renew a stale authorization for the still-owed <paramref name="amount"/>.</summary>
    Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, CancellationToken ct);

    /// <summary>Capture (take) a previously-authorized payment at fulfilment. Idempotent via the key.</summary>
    Task<CaptureResult> CaptureAsync(string authorizationId, string idempotencyKey, CancellationToken ct);

    /// <summary>Release a held authorization (cancel before fulfilment) — no money moves.</summary>
    Task VoidAsync(string authorizationId, CancellationToken ct);

    /// <summary>
    /// Refund a capture, in full (<paramref name="amount"/> null) or in part. The caller-supplied
    /// <paramref name="idempotencyKey"/> guarantees a repeated request never refunds twice.
    /// </summary>
    Task<RefundResult> RefundAsync(string captureId, decimal? amount, string idempotencyKey, CancellationToken ct);

    /// <summary>Vault (save) a card for a shopper and return the token id and a safe descriptor.</summary>
    Task<VaultCardResult> VaultCardAsync(string customerId, CardDetails card, CancellationToken ct);

    /// <summary>Delete a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken ct);

    /// <summary>
    /// PayPal's own record of transactions across a date range, covering the whole range (all pages,
    /// chunked to PayPal's per-request window limit), for reconciliation against eShop orders.
    /// </summary>
    Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct);
}
