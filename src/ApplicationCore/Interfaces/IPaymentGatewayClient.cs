using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Low-level client for the payment gateway (PayPal). Implementations must never
/// persist or log full card details.
/// </summary>
public interface IPaymentGatewayClient
{
    /// <summary>
    /// Creates a gateway order with AUTHORIZE intent. Returns the gateway order id.
    /// referenceId is echoed as custom_id; invoiceId must be unique per transaction.
    /// </summary>
    Task<string> CreateOrderAsync(decimal amount, string currency, string referenceId, string invoiceId, string idempotencyKey, CancellationToken cancellationToken = default);

    Task<GatewayAuthorization> AuthorizeWithCardAsync(string gatewayOrderId, CardDetails card, string idempotencyKey, CancellationToken cancellationToken = default);

    Task<GatewayAuthorization> AuthorizeWithVaultedCardAsync(string gatewayOrderId, string paymentTokenId, string idempotencyKey, CancellationToken cancellationToken = default);

    Task<GatewayAuthorization> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    Task<GatewayCapture> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default);

    Task<GatewayAuthorization> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default);

    Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default);

    Task<GatewayRefund> RefundCaptureAsync(string captureId, decimal? amount, string currency, string idempotencyKey, string? note, CancellationToken cancellationToken = default);

    Task<GatewayVaultedCard> VaultCardAsync(CardDetails card, string customerId, string idempotencyKey, CancellationToken cancellationToken = default);

    Task DeleteVaultedCardAsync(string paymentTokenId, CancellationToken cancellationToken = default);

    /// <summary>Returns all gateway transactions in [from, to], paging and chunking as required by the gateway.</summary>
    Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
