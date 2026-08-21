using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstracts the PayPal payment processor. The implementation lives in Infrastructure and is the only
/// place that talks to the PayPal SDK; the domain and the API work only against these plain contracts.
/// Every method may throw <see cref="Microsoft.eShopWeb.ApplicationCore.Exceptions.PaymentGatewayException"/>
/// (or its <c>BuyerActionRequiredException</c> subtype) on failure.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>
    /// Creates a PayPal order for the amount and places a hold (authorization) on the funds — the money is
    /// NOT taken yet. Pays either with a one-off card or a vaulted card. <paramref name="idempotencyKey"/>
    /// is passed to PayPal so a repeat of the same request never authorizes twice.
    /// </summary>
    Task<GatewayAuthorization> AuthorizeOrderAsync(
        decimal amount, string currencyCode, PaymentInstrument instrument, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Reads the current state of an authorization from PayPal.</summary>
    Task<GatewayAuthorizationState> GetAuthorizationAsync(string authorizationId, CancellationToken ct = default);

    /// <summary>
    /// Captures a held authorization — this is when the money is actually taken. Returns what PayPal
    /// reported: captured amount, PayPal fee and net proceeds.
    /// </summary>
    Task<GatewayCapture> CaptureAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default);

    /// <summary>
    /// Renews a stale authorization, yielding a fresh authorization id and honor period. Throws a
    /// <c>PaymentGatewayException</c> in operator-actionable terms when the hold can no longer be renewed.
    /// </summary>
    Task<GatewayAuthorizationState> ReauthorizeAsync(
        string authorizationId, decimal amount, string currencyCode, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Voids a held authorization, releasing the funds (cancel before fulfilment).</summary>
    Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default);

    /// <summary>
    /// Refunds a captured payment, in full (<paramref name="amount"/> null) or in part. The
    /// <paramref name="idempotencyKey"/> is the caller-supplied key: repeating it never refunds twice.
    /// </summary>
    Task<GatewayRefund> RefundAsync(
        string captureId, decimal? amount, string currencyCode, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Vaults a card for reuse and returns its reusable token id plus a safe descriptor.</summary>
    Task<GatewayVaultedCard> VaultCardAsync(GatewayCard card, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Deletes a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct = default);

    /// <summary>
    /// Lists PayPal's own record of transactions across the whole [from, to] range (paging and any
    /// window-chunking handled internally), for reconciliation against eShop orders.
    /// </summary>
    Task<IReadOnlyList<ReconciliationTransaction>> SearchTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
