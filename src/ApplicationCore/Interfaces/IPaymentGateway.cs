using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The payment-provider boundary. Implemented in Infrastructure by the PayPal gateway; the
/// rest of the application only ever sees these SDK-agnostic shapes. No full card details
/// ever appear in a return value.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>Creates a PayPal order (intent=AUTHORIZE) with a raw card and authorizes the hold.</summary>
    Task<GatewayAuthorizationResult> AuthorizeCardPaymentAsync(
        int orderId, decimal amount, string currency, CardPaymentDetails card, string idempotencyKey, CancellationToken ct);

    /// <summary>Creates a PayPal order (intent=AUTHORIZE) with a vaulted card and authorizes the hold.</summary>
    Task<GatewayAuthorizationResult> AuthorizeSavedCardPaymentAsync(
        int orderId, decimal amount, string currency, string vaultPaymentTokenId, string idempotencyKey, CancellationToken ct);

    Task<GatewayAuthorizationStatus> GetAuthorizationAsync(string authorizationId, CancellationToken ct);

    /// <summary>Renews a stale authorization. Throws PaymentGatewayException when it cannot be renewed.</summary>
    Task<GatewayAuthorizationStatus> ReauthorizeAsync(
        string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken ct);

    /// <summary>Captures an authorization in full; returns gross/fee/net as reported by the provider.</summary>
    Task<GatewayCaptureResult> CaptureAsync(string authorizationId, int orderId, string idempotencyKey, CancellationToken ct);

    /// <summary>Releases a hold without any money moving.</summary>
    Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct);

    /// <summary>Refunds a capture, in full or in part, under the caller-supplied idempotency key.</summary>
    Task<GatewayRefundResult> RefundAsync(
        string captureId, int orderId, decimal amount, string currency, string idempotencyKey, string? note, CancellationToken ct);

    /// <summary>Vaults a card server-side and returns only its safe display attributes.</summary>
    Task<GatewayVaultedCard> VaultCardAsync(CardPaymentDetails card, string merchantCustomerId, string idempotencyKey, CancellationToken ct);

    Task DeleteVaultedCardAsync(string paymentTokenId, CancellationToken ct);

    /// <summary>PayPal's own record of transactions over [from, to]; covers the whole range (all pages).</summary>
    Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
