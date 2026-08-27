using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the payment provider (PayPal). Implementations must never
/// persist or log full card details.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>Creates a provider order with intent=AUTHORIZE and authorizes it (holds the funds).</summary>
    Task<GatewayAuthorizationResult> AuthorizeCardPaymentAsync(decimal amount, string currency, CardDetails card,
        string? customId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Creates a provider order with intent=AUTHORIZE paid with a vaulted card, and authorizes it.</summary>
    Task<GatewayAuthorizationResult> AuthorizeVaultedCardPaymentAsync(decimal amount, string currency, string vaultTokenId,
        string? customId, string idempotencyKey, CancellationToken cancellationToken = default);

    Task<GatewayAuthorizationDetails> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Captures (takes) previously authorized funds.</summary>
    Task<GatewayCaptureResult> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currency,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Renews a stale authorization. Returns a new authorization id.</summary>
    Task<GatewayAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Voids an authorization, releasing the held funds.</summary>
    Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Refunds a captured payment, in full (amount null) or in part.</summary>
    Task<GatewayRefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currency,
        string idempotencyKey, string? noteToPayer, CancellationToken cancellationToken = default);

    /// <summary>Vaults a card and returns the token plus safe display data.</summary>
    Task<GatewayVaultedCard> VaultCardAsync(CardDetails card, string idempotencyKey, CancellationToken cancellationToken = default);

    Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken cancellationToken = default);

    /// <summary>Lists the provider's own record of transactions for a range (one page).</summary>
    Task<GatewayTransactionPage> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        int page, int pageSize, CancellationToken cancellationToken = default);
}
