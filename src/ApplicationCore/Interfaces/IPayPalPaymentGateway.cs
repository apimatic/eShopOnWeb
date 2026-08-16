using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Paypal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Everything this app needs from PayPal, expressed in domain terms. The concrete implementation
/// talks to PayPal's Orders v2 / Payments v2 / Vault v3 / Reporting APIs over HTTPS.
/// </summary>
public interface IPayPalPaymentGateway
{
    /// <summary>
    /// Authorizes an order total (places a hold; does not capture) using a one-off card or a saved
    /// vaulted card. Throws <see cref="PaymentApprovalRequiredException"/> if PayPal demands a
    /// browser approval, and <see cref="PayPalApiException"/> for declines/other failures.
    /// </summary>
    Task<PayPalAuthorizationResult> AuthorizeOrderAsync(PayPalAuthorizationRequest request, CancellationToken ct = default);

    /// <summary>Captures a previously created authorization — the money is actually taken.</summary>
    Task<PayPalCaptureResult> CaptureAuthorizationAsync(string authorizationId, decimal amount,
        string currencyCode, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Voids an authorization, releasing the held funds so no money moves.</summary>
    Task VoidAuthorizationAsync(string authorizationId, CancellationToken ct = default);

    /// <summary>
    /// Re-authorizes a stale hold, returning a fresh authorization. Throws
    /// <see cref="AuthorizationNotRenewableException"/> when PayPal will not renew it.
    /// </summary>
    Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount,
        string currencyCode, CancellationToken ct = default);

    /// <summary>Reads the current state of an authorization (used to detect staleness).</summary>
    Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId, CancellationToken ct = default);

    /// <summary>
    /// Refunds a capture, in full (<paramref name="amount"/> null) or in part. The
    /// <paramref name="idempotencyKey"/> makes a repeat under the same key a no-op at PayPal too.
    /// </summary>
    Task<PayPalRefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currencyCode,
        string idempotencyKey, CancellationToken ct = default);

    /// <summary>Saves a card to PayPal's vault without taking a payment, returning the vault token.</summary>
    Task<PayPalVaultResult> VaultCardAsync(PayPalCardDetails card, CancellationToken ct = default);

    /// <summary>Removes a card from PayPal's vault so it can no longer be charged.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct = default);

    /// <summary>
    /// Lists PayPal's own record of transactions across the whole date range (following pagination),
    /// for reconciliation against eShop orders.
    /// </summary>
    Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct = default);
}
