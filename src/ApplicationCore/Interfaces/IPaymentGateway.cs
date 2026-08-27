using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the payment processor. The implementation talks to PayPal;
/// all PayPal-owned ids and statuses flow through these DTOs.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>Creates an order with intent=AUTHORIZE and authorizes it (places the hold).</summary>
    Task<AuthorizationResult> AuthorizeOrderAsync(AuthorizationRequest request, CancellationToken cancellationToken = default);

    Task<AuthorizationDetails> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Captures an authorization (takes the money). finalCapture releases any remaining hold.</summary>
    Task<CaptureResult> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currency,
        string? invoiceId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Renews a stale authorization hold.</summary>
    Task<AuthorizationDetails> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Voids an authorization, releasing the shopper's held funds.</summary>
    Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Refunds a capture, in full (amount null) or in part.</summary>
    Task<RefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currency,
        string idempotencyKey, string? noteToPayer, CancellationToken cancellationToken = default);

    /// <summary>Vaults a card for the given customer and returns safe display data only.</summary>
    Task<VaultedCardResult> VaultCardAsync(string customerId, CardDetails card, string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists PayPal's own record of transactions over [from, to], covering the whole range
    /// (chunked and paged internally).
    /// </summary>
    Task<IReadOnlyList<GatewayTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
