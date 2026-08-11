using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Typed client for the PayPal REST APIs, built against the PayPal OpenAPI specification.
/// Speaks the domain's language (decimals, plain ids) and hides the raw JSON contract and
/// OAuth token handling. Every call targets the configured PayPal environment/base URL.
/// </summary>
public interface IPayPalGateway
{
    /// <summary>
    /// Creates a PayPal Checkout order for the amount and authorizes it against the supplied
    /// card or vaulted card, placing a hold on the money without taking it. Idempotent on
    /// <see cref="AuthorizeOrderRequest.IdempotencyKey"/>.
    /// </summary>
    Task<AuthorizeOrderResult> AuthorizeOrderAsync(AuthorizeOrderRequest request, CancellationToken cancellationToken = default);

    /// <summary>Captures an existing authorization — this is when the money is actually taken.</summary>
    Task<CaptureResult> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currencyCode,
        string invoiceId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Renews a stale authorization so a fulfilment can still capture it.</summary>
    Task<ReauthorizeResult> ReauthorizeAsync(string authorizationId, decimal amount, string currencyCode,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Voids an authorization before capture, releasing the held funds.</summary>
    Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refunds a captured payment, in full (<paramref name="amount"/> null) or in part.
    /// Idempotent on <paramref name="idempotencyKey"/>.
    /// </summary>
    Task<RefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currencyCode,
        string invoiceId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Vaults a card for later reuse and returns the token id and a safe descriptor.</summary>
    Task<VaultCardResult> VaultCardAsync(VaultCardRequest request, CancellationToken cancellationToken = default);

    /// <summary>Deletes a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns PayPal's own record of transactions across the whole date range, paging and
    /// chunking as required by the reporting API.
    /// </summary>
    Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
