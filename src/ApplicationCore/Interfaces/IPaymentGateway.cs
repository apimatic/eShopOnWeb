using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The payment provider boundary. All PayPal SDK interaction sits behind this interface;
/// implementations translate provider errors into PaymentGatewayException / PaymentDeclinedException.
/// </summary>
public interface IPaymentGateway
{
    Task<GatewayOrder> CreateOrderAsync(decimal amount, string currency, string referenceId,
        string invoiceId, string customId, string idempotencyKey, CancellationToken ct = default);

    Task<GatewayAuthorization> AuthorizeOrderAsync(string payPalOrderId, CardDetails? card,
        string? vaultPaymentTokenId, string idempotencyKey, CancellationToken ct = default);

    Task<GatewayAuthorizationState> GetAuthorizationAsync(string authorizationId, CancellationToken ct = default);

    Task<GatewayAuthorizationState> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        string idempotencyKey, CancellationToken ct = default);

    Task<GatewayCapture> CaptureAuthorizationAsync(string authorizationId, string invoiceId,
        string idempotencyKey, CancellationToken ct = default);

    Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default);

    Task<GatewayRefund> RefundCaptureAsync(string captureId, decimal? amount, string currency,
        string idempotencyKey, CancellationToken ct = default);

    Task<GatewayVaultedCard> VaultCardAsync(CardDetails card, string? payPalCustomerId, string merchantCustomerId,
        string idempotencyKey, CancellationToken ct = default);

    Task<IReadOnlyList<GatewayVaultedCard>> ListVaultedCardsAsync(string payPalCustomerId, CancellationToken ct = default);

    Task DeleteVaultedCardAsync(string paymentTokenId, CancellationToken ct = default);

    Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct = default);
}
