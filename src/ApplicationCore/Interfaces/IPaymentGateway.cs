using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the payment processor (PayPal). Implementations must follow the
/// processor's OpenAPI contract; only processor-owned ids/statuses flow back.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>Creates a processor order with AUTHORIZE intent for the given amount.</summary>
    Task<GatewayOrder> CreateOrderAsync(string referenceId, decimal amount, string currency, string idempotencyKey);

    /// <summary>Authorizes (holds) the order amount using one-off card details.</summary>
    Task<GatewayAuthorization> AuthorizeOrderWithCardAsync(string gatewayOrderId, GatewayCardDetails card, string idempotencyKey);

    /// <summary>Authorizes (holds) the order amount using a vaulted card.</summary>
    Task<GatewayAuthorization> AuthorizeOrderWithVaultedCardAsync(string gatewayOrderId, string vaultTokenId, string idempotencyKey);

    Task<GatewayAuthorization> GetAuthorizationAsync(string authorizationId);

    /// <summary>Renews a stale authorization for the given amount.</summary>
    Task<GatewayAuthorization> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string idempotencyKey);

    /// <summary>Captures (takes) the money held by an authorization.</summary>
    Task<GatewayCapture> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currency, string invoiceId, string idempotencyKey);

    /// <summary>Releases the hold without moving money.</summary>
    Task<GatewayAuthorization> VoidAuthorizationAsync(string authorizationId, string idempotencyKey);

    /// <summary>Refunds a capture, in full (amount null) or in part.</summary>
    Task<GatewayRefund> RefundCaptureAsync(string captureId, decimal? amount, string currency, string idempotencyKey, string? noteToPayer);

    /// <summary>Vaults a card and returns its token plus safe display attributes.</summary>
    Task<GatewayVaultedCard> SaveCardAsync(string merchantCustomerId, GatewayCardDetails card, string idempotencyKey);

    Task DeleteSavedCardAsync(string vaultTokenId);

    /// <summary>Returns the processor's own record of transactions over the whole range (all pages).</summary>
    Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to);
}
