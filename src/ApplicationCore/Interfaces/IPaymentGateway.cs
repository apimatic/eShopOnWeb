using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the payment provider (PayPal). Implementations live in Infrastructure.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>Authorize (hold) an amount on a raw card or a vaulted payment token.</summary>
    Task<GatewayAuthorization> AuthorizeAsync(GatewayAuthorizeRequest request, CancellationToken ct = default);

    /// <summary>Capture a previously authorized hold - this is when money moves.</summary>
    Task<GatewayCapture> CaptureAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Read the current state of an authorization.</summary>
    Task<GatewayAuthorizationState> GetAuthorizationAsync(string authorizationId, CancellationToken ct = default);

    /// <summary>Renew a stale authorization (provider window: day 4 to day 29 after approval).</summary>
    Task<GatewayAuthorizationState> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Void an authorization, releasing the shopper's held funds.</summary>
    Task<GatewayAuthorizationState> VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Refund a capture, in full (amount null) or in part.</summary>
    Task<GatewayRefund> RefundAsync(string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Vault a card for a shopper and return the token plus safe display data.</summary>
    Task<GatewayVaultedCard> VaultCardAsync(GatewayCardDetails card, string merchantCustomerId, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Delete a vaulted payment token.</summary>
    Task DeletePaymentTokenAsync(string paymentTokenId, CancellationToken ct = default);

    /// <summary>Search the provider's own record of transactions over a date range (all pages).</summary>
    Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
