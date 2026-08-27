using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Gateway-neutral view of the payment processor. The PayPal implementation
/// lives in Infrastructure; full card details pass through only and are never
/// persisted or logged.
/// </summary>
public interface IPayPalGateway
{
    Task<GatewayAuthorization> AuthorizeCardAsync(decimal amount, string currency, CardDetails card,
        string referenceId, string requestId, CancellationToken cancellationToken = default);

    Task<GatewayAuthorization> AuthorizeVaultedCardAsync(decimal amount, string currency, string vaultTokenId,
        string referenceId, string requestId, CancellationToken cancellationToken = default);

    Task<GatewayCapture> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken = default);

    Task<GatewayAuthorization> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken = default);

    Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    Task<GatewayRefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currency,
        string requestId, CancellationToken cancellationToken = default);

    Task<GatewayVaultedCard> VaultCardAsync(CardDetails card, string? payPalCustomerId,
        string requestId, CancellationToken cancellationToken = default);

    Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GatewayTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
