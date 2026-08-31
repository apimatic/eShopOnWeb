using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Port to the payment processor (PayPal). Every mutating call takes an
/// idempotency key so a retried request never moves money twice.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>Create an order at the gateway and authorize (hold) the amount in one call.</summary>
    Task<PaymentGatewayAuthorization> AuthorizeAsync(GatewayAuthorizeRequest request, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Read the current state of an authorization.</summary>
    Task<PaymentGatewayAuthorization> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Renew a stale authorization. The gateway issues a new authorization id.</summary>
    Task<PaymentGatewayAuthorization> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Take the money held by an authorization.</summary>
    Task<PaymentGatewayCapture> CaptureAsync(string authorizationId, decimal amount, string currency, string? invoiceId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Release the hold without any money moving.</summary>
    Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Refund a capture; amount null means refund in full.</summary>
    Task<PaymentGatewayRefund> RefundCaptureAsync(string captureId, decimal? amount, string currency, string? invoiceId, string? noteToPayer, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Vault a card for later use. Returns gateway ids and safe display data only.</summary>
    Task<VaultedCardResult> VaultCardAsync(GatewayCardDetails card, string? gatewayCustomerId, string idempotencyKey, CancellationToken cancellationToken = default);

    Task DeleteVaultedCardAsync(string paymentTokenId, CancellationToken cancellationToken = default);

    /// <summary>The gateway's own record of transactions over a range; covers the whole range (all pages).</summary>
    Task<IReadOnlyList<GatewayTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
