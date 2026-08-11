using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the PayPal REST API. Implemented in the Infrastructure layer against PayPal's sandbox/live
/// hosts. All calls are server-to-server; card details passed in are transient and never persisted or logged.
/// </summary>
public interface IPayPalClient
{
    /// <summary>The configured settlement currency (from PayPal:Currency).</summary>
    string Currency { get; }

    /// <summary>Create a PayPal order with intent=AUTHORIZE paying with a raw card, placing a hold for the amount.</summary>
    Task<AuthorizationOutcome> CreateAuthorizedOrderWithCardAsync(
        Money amount, CardDetails card, string referenceId, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Create a PayPal order with intent=AUTHORIZE paying with a previously vaulted card token.</summary>
    Task<AuthorizationOutcome> CreateAuthorizedOrderWithVaultAsync(
        Money amount, string vaultId, string referenceId, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Read the current PayPal-side state of an authorization.</summary>
    Task<AuthorizationSnapshot> GetAuthorizationAsync(string authorizationId, CancellationToken ct = default);

    /// <summary>Capture an authorization, taking the money. Returns the fee breakdown PayPal reports.</summary>
    Task<CaptureOutcome> CaptureAuthorizationAsync(
        string authorizationId, Money amount, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Re-authorize a stale authorization, producing a fresh hold (new id, new honor period).</summary>
    Task<AuthorizationOutcome> ReauthorizeAsync(
        string authorizationId, Money amount, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Void (cancel) an authorization, releasing the held funds.</summary>
    Task VoidAuthorizationAsync(string authorizationId, CancellationToken ct = default);

    /// <summary>Refund a capture. A null amount refunds the full remaining amount; otherwise a partial refund.</summary>
    Task<RefundOutcome> RefundCaptureAsync(
        string captureId, Money? amount, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Vault (save) a card and return the reusable token and a safe descriptor.</summary>
    Task<VaultedCardResult> VaultCardAsync(CardDetails card, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Delete a vaulted card token so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct = default);

    /// <summary>
    /// List PayPal's own record of transactions across a date range, paging and chunking as needed to cover
    /// the whole range (PayPal caps a single query at 31 days and 500 records per page).
    /// </summary>
    Task<IReadOnlyList<PayPalTransactionRecord>> SearchTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
