using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPayPalGateway
{
    string Currency { get; }

    Task<AuthorizationResult> AuthorizeCardAsync(
        int orderId,
        decimal amount,
        CardPaymentDetails card,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<AuthorizationResult> AuthorizeVaultedCardAsync(
        int orderId,
        decimal amount,
        string vaultId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<AuthorizationResult> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task VoidAuthorizationAsync(
        string authorizationId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<CaptureResult> CaptureAuthorizationAsync(
        string authorizationId,
        decimal amount,
        string invoiceId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<RefundGatewayResult> RefundCaptureAsync(
        string captureId,
        decimal? amount,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<VaultedCardResult> VaultCardAsync(
        CardPaymentDetails card,
        string? existingPayPalCustomerId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ReportedTransaction>> ListTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
