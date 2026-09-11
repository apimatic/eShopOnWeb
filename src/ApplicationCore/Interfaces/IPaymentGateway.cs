using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the payment processor (PayPal). The application core depends only on this
/// interface and the plain DTOs in <see cref="Microsoft.eShopWeb.ApplicationCore.Payments"/>;
/// all PayPal wire details (OpenAPI request/response shapes, auth, base URL) live in Infrastructure.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>
    /// Place a hold for the given amount (create a PayPal order with intent=AUTHORIZE using a card
    /// or a vaulted card). Does not capture. <paramref name="idempotencyKey"/> is sent as
    /// PayPal-Request-Id so a repeat never places a second hold.
    /// </summary>
    Task<GatewayAuthorizationResult> AuthorizeAsync(GatewayAuthorizeRequest request, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Capture a previously authorized payment (money is actually taken).</summary>
    Task<GatewayCaptureResult> CaptureAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Look up the current state of an authorization (used to detect staleness before capture).</summary>
    Task<GatewayAuthorizationState> GetAuthorizationAsync(string authorizationId, CancellationToken ct = default);

    /// <summary>Renew a stale authorization, returning the new hold. Throws if it cannot be renewed.</summary>
    Task<GatewayAuthorizationState> ReauthorizeAsync(string authorizationId, decimal amount, string currencyCode, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Void an authorization before capture, releasing the held funds.</summary>
    Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Refund a capture, in full or in part. <paramref name="amount"/> null means full refund.</summary>
    Task<GatewayRefundResult> RefundAsync(string captureId, decimal? amount, string currencyCode, string? invoiceId, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Vault (save) a card and return the vault token id plus a safe descriptor.</summary>
    Task<GatewayVaultResult> VaultCardAsync(GatewayCardDetails card, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Delete a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct = default);

    /// <summary>
    /// List PayPal's own record of transactions across the whole date range (chunked and paged
    /// internally so the result covers the full range, not just the first page).
    /// </summary>
    Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
