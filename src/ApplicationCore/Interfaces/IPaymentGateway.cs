using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the payment processor (PayPal). All money-movement operations accept
/// a caller-supplied idempotency key which is forwarded to the processor so that retries
/// of the same logical operation never move money twice.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>
    /// Creates a processor order for the given amount and authorizes (holds) the funds,
    /// either with full card details or with a vaulted card token.
    /// </summary>
    Task<GatewayAuthorization> AuthorizeAsync(string invoiceId, string? customId, decimal amount, string currency,
        CardDetails? card, string? vaultPaymentTokenId, string idempotencyKey, CancellationToken cancellationToken = default);

    Task<GatewayAuthorizationInfo> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    Task<GatewayAuthorizationInfo> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        string idempotencyKey, CancellationToken cancellationToken = default);

    Task<GatewayCapture> CaptureAsync(string authorizationId, decimal amount, string currency, string invoiceId,
        string idempotencyKey, CancellationToken cancellationToken = default);

    Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default);

    Task<GatewayRefund> RefundAsync(string captureId, decimal? amount, string currency, string? customId,
        string idempotencyKey, string? note, CancellationToken cancellationToken = default);

    Task<GatewayVaultedCard> VaultCardAsync(CardDetails card, string customerId, string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task DeletePaymentTokenAsync(string paymentTokenId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the processor's own record of transactions over the whole [from, to] range,
    /// following pagination and the processor's maximum window internally.
    /// </summary>
    Task<IReadOnlyList<GatewayTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
