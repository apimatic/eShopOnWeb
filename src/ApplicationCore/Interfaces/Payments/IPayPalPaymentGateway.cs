using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

/// <summary>
/// Abstraction over the PayPal REST APIs the app needs. The implementation is built directly
/// against PayPal's OpenAPI specification (Checkout Orders v2, Payments v2, Vault v3,
/// Transaction Search v1). Every operation is idempotent when given a stable idempotency key.
/// </summary>
public interface IPayPalPaymentGateway
{
    /// <summary>
    /// Creates a PayPal order with AUTHORIZE intent and processes the supplied card (or vaulted
    /// card), placing a hold equal to the order total. Throws <see cref="PayPalChallengeRequiredException"/>
    /// if PayPal demands a browser approval, and <see cref="PayPalGatewayException"/> on decline/error.
    /// </summary>
    Task<GatewayAuthorization> AuthorizeOrderAsync(AuthorizeOrderRequest request, CancellationToken cancellationToken = default);

    /// <summary>Captures (takes) money against an existing authorization.</summary>
    Task<GatewayCapture> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currencyCode, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Renews a stale authorization, returning a fresh hold that can be captured.</summary>
    Task<GatewayAuthorizationInfo> ReauthorizeAsync(string authorizationId, decimal amount, string currencyCode, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Reads the current status/expiry of an authorization.</summary>
    Task<GatewayAuthorizationInfo> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Voids an authorization, releasing the held funds. No money moves.</summary>
    Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Refunds a capture, in full (null amount) or in part.</summary>
    Task<GatewayRefund> RefundCaptureAsync(string captureId, decimal? amount, string currencyCode, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Vaults a card, returning a reusable token and safe descriptors.</summary>
    Task<GatewayVaultedCard> VaultCardAsync(VaultCardRequest request, CancellationToken cancellationToken = default);

    /// <summary>Deletes a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns every transaction PayPal reports for the range, following pagination across the
    /// whole range (not just the first page).
    /// </summary>
    Task<IReadOnlyList<ReconciliationTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
