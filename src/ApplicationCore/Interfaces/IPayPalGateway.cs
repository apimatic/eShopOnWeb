using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The single seam that talks to PayPal. Implementations wrap the PayPal Server SDK and translate every
/// failure into an <see cref="Exceptions.PaymentException"/>; callers never see an SDK type.
/// </summary>
public interface IPayPalGateway
{
    /// <summary>Creates a PayPal order with intent=AUTHORIZE and the given funding source, and places the hold.</summary>
    Task<AuthorizationResult> AuthorizeAsync(AuthorizeGatewayRequest request, CancellationToken ct);

    /// <summary>Captures the full authorized amount. Idempotent at PayPal via <paramref name="requestId"/>.</summary>
    Task<CaptureResult> CaptureAsync(string authorizationId, string requestId, CancellationToken ct);

    /// <summary>Renews a stale authorization; returns the (possibly new) authorization id.</summary>
    Task<AuthorizationInfo> ReauthorizeAsync(string authorizationId, string requestId, CancellationToken ct);

    /// <summary>Reads an authorization's current status (freshness / capture re-read).</summary>
    Task<AuthorizationInfo> GetAuthorizationAsync(string authorizationId, CancellationToken ct);

    /// <summary>Releases a held authorization (cancel before fulfilment).</summary>
    Task VoidAsync(string authorizationId, CancellationToken ct);

    /// <summary>Refunds a capture; null amount = full refund, a value = partial. Idempotent via <paramref name="idempotencyKey"/>.</summary>
    Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken ct);

    /// <summary>Reads a capture's current status (refund re-read).</summary>
    Task<CaptureResult> GetCaptureAsync(string captureId, CancellationToken ct);

    /// <summary>Vaults a card and returns a safe descriptor + the vault token id.</summary>
    Task<VaultCardResult> VaultCardAsync(CardDetails card, string merchantCustomerId, string? existingCustomerId,
        string requestId, CancellationToken ct);

    /// <summary>Deletes a vaulted card token so it can no longer be used to pay.</summary>
    Task DeleteVaultTokenAsync(string tokenId, CancellationToken ct);

    /// <summary>Lists PayPal's own transactions across the whole date range (all pages).</summary>
    Task<ReconciliationFetch> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
