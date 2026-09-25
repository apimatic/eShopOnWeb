using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The application's boundary to PayPal. Every PayPal interaction goes through this gateway, whose
/// implementation owns the SDK, translates all provider/transport failures into <see cref="PaymentException"/>,
/// and exposes only app types (no SDK types leak out). Idempotency keys are passed in by the caller so the
/// gateway sends PayPal a real caller-supplied key on each write.
/// </summary>
public interface IPayPalGateway
{
    /// <summary>Hold the order total (authorize, do not capture). Creates the PayPal order and authorizes it with the given card/vault instrument.</summary>
    Task<AuthorizationResult> AuthorizeOrderAsync(
        string invoiceId, decimal amount, string currencyCode, PaymentInstrument instrument,
        string idempotencyBase, CancellationToken ct);

    /// <summary>Read the current state of an authorization (for renewal decisions and settling unknown outcomes).</summary>
    Task<AuthorizationStatusInfo> GetAuthorizationAsync(string authorizationId, CancellationToken ct);

    /// <summary>Renew (reauthorize) a stale authorization so fulfilment can still capture it.</summary>
    Task<RenewalResult> ReauthorizeAsync(string authorizationId, decimal amount, string currencyCode, string idempotencyKey, CancellationToken ct);

    /// <summary>Take the money: capture an authorized payment. Returns what PayPal reported (captured amount, fee, net).</summary>
    Task<CaptureResult> CaptureAsync(string authorizationId, decimal amount, string currencyCode, string invoiceId, string idempotencyKey, CancellationToken ct);

    /// <summary>Release a held authorization (cancel before fulfilment — no money moves).</summary>
    Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct);

    /// <summary>Refund a captured payment, in full (<paramref name="amount"/> null) or in part.</summary>
    Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currencyCode, string idempotencyKey, CancellationToken ct);

    /// <summary>Vault a card for reuse; returns the token id and a safe display of the card.</summary>
    Task<VaultCardResult> VaultCardAsync(string customerId, CardDetails card, string idempotencyKey, CancellationToken ct);

    /// <summary>Remove a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultCardAsync(string vaultId, CancellationToken ct);

    /// <summary>List PayPal's own transactions for a date range, walking every page (up to <paramref name="maxPages"/>).</summary>
    Task<TransactionSearchResult> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, string currencyCode, int maxPages, CancellationToken ct);
}
