using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// The application's view of everything it needs from PayPal. The implementation lives in the
/// Infrastructure layer and is the only place that talks to the PayPal SDK.
/// </summary>
public interface IPayPalGateway
{
    /// <summary>
    /// Creates a PayPal order with intent=AUTHORIZE, pays it directly with a raw card, and places
    /// an authorization hold for the exact amount. No money is captured. <paramref name="requestId"/>
    /// is an idempotency key so a double-click never authorizes twice.
    /// </summary>
    Task<AuthorizationResult> AuthorizeWithCardAsync(
        decimal amount, string currency, CardDetails card, string requestId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Same as <see cref="AuthorizeWithCardAsync"/> but pays with a previously vaulted card token.
    /// </summary>
    Task<AuthorizationResult> AuthorizeWithVaultedCardAsync(
        decimal amount, string currency, string vaultId, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Reads the current status of an authorization from PayPal.</summary>
    Task<AuthorizationStatus> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Captures (takes the money for) a previously created authorization.</summary>
    Task<CaptureResult> CaptureAuthorizationAsync(
        string authorizationId, decimal amount, string currency, string requestId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renews a stale authorization so it can still be captured. Throws
    /// <see cref="PayPalAuthorizationUnrenewableException"/> when PayPal will no longer renew it.
    /// </summary>
    Task<ReauthorizationResult> ReauthorizeAsync(
        string authorizationId, decimal amount, string currency, CancellationToken cancellationToken = default);

    /// <summary>Voids an authorization, releasing the hold. Used to cancel before fulfilment.</summary>
    Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refunds a capture, in full (amount null) or in part. <paramref name="requestId"/> is the
    /// caller-supplied idempotency key so a repeated request never refunds twice.
    /// </summary>
    Task<RefundResult> RefundCaptureAsync(
        string captureId, decimal? amount, string currency, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Vaults a card for later reuse and returns its safe description plus the vault token id.</summary>
    Task<VaultCardResult> VaultCardAsync(CardDetails card, CancellationToken cancellationToken = default);

    /// <summary>Removes a card from the PayPal vault so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists PayPal's own record of transactions across the whole date range, paging through every
    /// page (not just the first).
    /// </summary>
    Task<TransactionSearchResult> ListTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
