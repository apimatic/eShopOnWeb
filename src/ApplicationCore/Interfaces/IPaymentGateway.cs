using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payment;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPaymentGateway
{
    string Currency { get; }

    Task<AuthorizationResult> AuthorizeAsync(
        int orderId,
        decimal amount,
        CardPaymentInput? card,
        string? vaultId,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<AuthorizationResult> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<AuthorizationResult> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken);

    Task<CaptureResult> CaptureAsync(
        string authorizationId,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task VoidAsync(
        string authorizationId,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<RefundResult> RefundAsync(
        string captureId,
        decimal? amount,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<VaultResult> VaultCardAsync(
        string merchantCustomerId,
        CardPaymentInput card,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken);

    Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);
}
