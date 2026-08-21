using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPaymentGateway
{
    Task<AuthorizationResult> AuthorizeCardAsync(
        int orderId,
        decimal amount,
        string currency,
        CardDetails card,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<AuthorizationResult> AuthorizeVaultedCardAsync(
        int orderId,
        decimal amount,
        string currency,
        string vaultId,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<AuthorizationSnapshot> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken);

    Task<AuthorizationSnapshot> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<CaptureResult> CaptureAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task VoidAsync(
        string authorizationId,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<RefundResult> RefundAsync(
        string captureId,
        decimal? amount,
        string currency,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<VaultedCardResult> SaveCardAsync(
        string merchantCustomerId,
        CardDetails card,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task DeleteVaultedCardAsync(string paymentTokenId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ReportedTransaction>> SearchTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);
}
