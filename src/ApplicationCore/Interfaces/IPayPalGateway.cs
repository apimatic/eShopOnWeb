using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Low-level access to PayPal's REST API. All money movement goes through here. Implemented in
/// Infrastructure over HttpClient; the plugin's best-practices reference is the source for shapes.
/// </summary>
public interface IPayPalGateway
{
    /// <summary>The configured ISO-4217 currency (from PayPal:Currency) used for all amounts.</summary>
    string Currency { get; }

    /// <summary>
    /// Creates a PayPal order with intent=AUTHORIZE and processes the card (raw or vaulted),
    /// putting a hold on the funds equal to the amount. Idempotent per <paramref name="idempotencyKey"/>.
    /// </summary>
    Task<PayPalAuthorizationResult> AuthorizeAsync(PayPalAuthorizeRequest request, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Reads the current state of an authorization.</summary>
    Task<PayPalAuthorizationState> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Captures (takes) an authorized amount. Returns PayPal's fee/net breakdown.</summary>
    Task<PayPalCaptureResult> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Renews a stale authorization, producing a fresh authorization for the amount.</summary>
    Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Voids (releases) an authorization so no money moves.</summary>
    Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Refunds a capture, in full (null amount) or in part. Idempotent per <paramref name="idempotencyKey"/>.</summary>
    Task<PayPalRefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currency, string idempotencyKey, string? noteToPayer, CancellationToken cancellationToken = default);

    /// <summary>Vaults a card, returning a reusable payment token and a safe description.</summary>
    Task<PayPalVaultResult> VaultCardAsync(PayPalCardDetails card, string? customerId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Deletes a vaulted card so it can no longer be charged.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists PayPal's own record of transactions across the whole [from,to] range, chunking to
    /// the API's 31-day limit and paging through every page.
    /// </summary>
    Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
