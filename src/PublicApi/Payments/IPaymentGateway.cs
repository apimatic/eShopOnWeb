using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public interface IPaymentGateway
{
    Task<GatewayAuthorizationResult> AuthorizeAsync(string invoiceId, decimal amount, string currency,
        GatewayCard? card, string? vaultId, string idempotencyKey, CancellationToken ct);

    Task<GatewayAuthorizationStatus> GetAuthorizationAsync(string authorizationId, CancellationToken ct);

    Task<GatewayAuthorizationStatus> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        string idempotencyKey, CancellationToken ct);

    Task<GatewayCaptureResult> CaptureAsync(string authorizationId, string invoiceId, string idempotencyKey, CancellationToken ct);

    Task<string> VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct);

    Task<GatewayRefundResult> RefundAsync(string captureId, decimal? amount, string currency,
        string idempotencyKey, CancellationToken ct);

    Task<GatewaySavedCardResult> SaveCardAsync(string customerId, GatewayCard card, string idempotencyKey, CancellationToken ct);

    Task DeleteCardAsync(string vaultTokenId, CancellationToken ct);

    Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
