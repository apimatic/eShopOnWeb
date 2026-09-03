using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The application's boundary to PayPal. The only place PayPal is called; all provider failures surface as
/// <see cref="Microsoft.eShopWeb.ApplicationCore.Exceptions.PayPalException"/>. Amounts are decimals in the
/// order's currency; the implementation formats them to the currency's precision. Every operation is
/// idempotent in effect: the implementation supplies a stable PayPal request id keyed on the identifiers
/// passed here (refunds additionally take the caller's own key).
/// </summary>
public interface IPayPalPaymentGateway
{
    /// <summary>
    /// Authorizes (holds) <paramref name="amount"/> for the order using either one-off card details or a
    /// previously vaulted card (<paramref name="vaultId"/>). Exactly one of <paramref name="card"/> /
    /// <paramref name="vaultId"/> is supplied. <paramref name="invoiceId"/> is the merchant invoice id and
    /// <paramref name="paymentReference"/> seeds the deterministic PayPal request ids (safe to repeat).
    /// </summary>
    Task<AuthorizeResult> AuthorizeAsync(int orderId, string invoiceId, string paymentReference, decimal amount,
        string currency, CardDetails? card, string? vaultId, CancellationToken ct);

    /// <summary>Captures the authorized payment at fulfilment. Returns the captured amount, fee and net.</summary>
    Task<CaptureResult> CaptureAsync(string authorizationId, string invoiceId, decimal amount, string currency,
        CancellationToken ct);

    /// <summary>Renews a stale authorization, returning a fresh authorization to capture against.</summary>
    Task<ReauthorizeResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        CancellationToken ct);

    /// <summary>Voids the authorization, releasing the held funds before fulfilment.</summary>
    Task VoidAsync(string authorizationId, CancellationToken ct);

    /// <summary>
    /// Refunds the captured payment, in full (<paramref name="amount"/> null) or in part. The
    /// caller-supplied <paramref name="idempotencyKey"/> makes a repeated request safe.
    /// </summary>
    Task<RefundResult> RefundAsync(string captureId, string invoiceId, decimal? amount, string currency,
        string idempotencyKey, CancellationToken ct);

    /// <summary>Vaults a card for the shopper and returns a safe description plus PayPal's vault token id.</summary>
    Task<VaultedCardResult> VaultCardAsync(string buyerId, CardDetails card, CancellationToken ct);

    /// <summary>Removes a vaulted card from PayPal by its vault token id.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct);

    /// <summary>
    /// Returns every PayPal-recorded transaction in the date range, walking all report pages. May be empty
    /// for a very recent range because PayPal transaction reporting lags live activity.
    /// </summary>
    Task<IReadOnlyList<ReconciliationTransaction>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken ct);
}
