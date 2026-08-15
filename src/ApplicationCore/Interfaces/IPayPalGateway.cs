using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The application's single seam onto PayPal. Every PayPal interaction goes through this
/// abstraction; the concrete implementation (Infrastructure) is the only place that talks to
/// the PayPal SDK. Raw card details flow in but are never returned or persisted.
/// </summary>
public interface IPayPalGateway
{
    /// <summary>
    /// Create a checkout order (intent = AUTHORIZE) and place the hold, paying with raw card
    /// details or a vaulted card. The held amount equals <see cref="PayPalAuthorizeRequest.Amount"/>.
    /// Throws <see cref="Exceptions.PayPalChallengeRequiredException"/> if PayPal demands a
    /// browser/3DS approval instead of authorizing directly.
    /// </summary>
    Task<PayPalAuthorization> AuthorizeAsync(PayPalAuthorizeRequest request, CancellationToken ct = default);

    /// <summary>Capture a previously placed hold at fulfilment; returns PayPal's money breakdown.</summary>
    Task<PayPalCapture> CaptureAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default);

    /// <summary>
    /// Renew a stale hold before capture. Throws
    /// <see cref="Exceptions.AuthorizationNotRenewableException"/> when PayPal reports the hold can
    /// no longer be reauthorized, carrying an operator-actionable reason.
    /// </summary>
    Task<PayPalAuthorization> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Release a hold before capture, so no money moves.</summary>
    Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Refund a capture, in full (null amount) or in part.</summary>
    Task<PayPalRefund> RefundAsync(string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Read a hold's current status/amount/expiry from PayPal.</summary>
    Task<PayPalAuthorization> GetAuthorizationAsync(string authorizationId, CancellationToken ct = default);

    /// <summary>Vault a card so it can be reused; returns a safe descriptor and the vault id.</summary>
    Task<PayPalVaultedCard> VaultCardAsync(CardDetails card, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Remove a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct = default);

    /// <summary>
    /// PayPal's own record of transactions across a date range, paged through to exhaustion.
    /// May legitimately be empty for very recent ranges (reporting lag).
    /// </summary>
    Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
