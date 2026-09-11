using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

/// <summary>
/// Port over the PayPal REST APIs used by this integration. The implementation builds every
/// request/response strictly to the PayPal OpenAPI specifications under <c>api-specs/paypal</c>.
/// </summary>
public interface IPayPalGateway
{
    /// <summary>The currency (ISO-4217) configured for this PayPal account, e.g. "USD".</summary>
    string CurrencyCode { get; }

    /// <summary>
    /// Creates a PayPal Order with intent AUTHORIZE and the given funding source, placing a hold
    /// for the order total. <paramref name="idempotencyKey"/> is sent as PayPal-Request-Id so a
    /// repeated call does not authorize twice.
    /// </summary>
    Task<PayPalAuthorization> AuthorizeOrderAsync(AuthorizeOrderInput input, string idempotencyKey, CancellationToken ct);

    /// <summary>Fetches the current state of an authorization (hold).</summary>
    Task<PayPalAuthorization> GetAuthorizationAsync(string authorizationId, CancellationToken ct);

    /// <summary>Renews a stale authorization, producing a fresh hold for the same amount.</summary>
    Task<PayPalAuthorization> ReauthorizeAsync(string authorizationId, decimal amount, string currencyCode, CancellationToken ct);

    /// <summary>Captures (takes) the full authorized amount. Idempotent via PayPal-Request-Id.</summary>
    Task<PayPalCapture> CaptureAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken ct);

    /// <summary>Voids (releases) an authorization before capture. Idempotent via PayPal-Request-Id.</summary>
    Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken ct);

    /// <summary>
    /// Refunds a capture, in full when <paramref name="amount"/> is null, otherwise the partial
    /// amount. <paramref name="idempotencyKey"/> is sent as PayPal-Request-Id.
    /// </summary>
    Task<PayPalRefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currencyCode, string idempotencyKey, CancellationToken ct);

    /// <summary>Vaults a card for reuse, returning a safe description and the vault token id.</summary>
    Task<VaultedCard> VaultCardAsync(CardDetails card, string? customerId, string idempotencyKey, CancellationToken ct);

    /// <summary>Deletes a vaulted card so it can no longer be charged.</summary>
    Task DeleteVaultedCardAsync(string tokenId, CancellationToken ct);

    /// <summary>
    /// Lists PayPal's own record of transactions across the whole range, paging and chunking as
    /// needed so nothing is missed.
    /// </summary>
    Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
