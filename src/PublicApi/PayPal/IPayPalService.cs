using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System;

namespace Microsoft.eShopWeb.PublicApi.PayPal;

public interface IPayPalService
{
    string Currency { get; }
    Task<AuthorizationResult> AuthorizeWithCardAsync(
        decimal amount, string currency, string idempotencyKey,
        CardPaymentDetails card, string? invoiceRef = null,
        CancellationToken ct = default);

    Task<AuthorizationResult> AuthorizeWithVaultAsync(
        decimal amount, string currency, string idempotencyKey,
        string vaultToken, string? invoiceRef = null,
        CancellationToken ct = default);

    Task<CaptureResult> CaptureAuthorizationAsync(
        string authorizationId, string idempotencyKey,
        CancellationToken ct = default);

    Task<bool> IsAuthorizationExpiredAsync(
        string authorizationId,
        CancellationToken ct = default);

    Task<ReauthorizeResult> ReauthorizeAsync(
        string authorizationId, decimal amount, string currency, string idempotencyKey,
        CancellationToken ct = default);

    Task VoidAuthorizationAsync(
        string authorizationId, string idempotencyKey,
        CancellationToken ct = default);

    Task<RefundResult> RefundCaptureAsync(
        string captureId, decimal? amount, string currency, string idempotencyKey,
        CancellationToken ct = default);

    Task<VaultResult> VaultCardAsync(
        string customerId, string idempotencyKey, CardPaymentDetails card,
        CancellationToken ct = default);

    Task<IReadOnlyList<VaultedPaymentMethodInfo>> ListVaultedCardsAsync(
        string customerId,
        CancellationToken ct = default);

    Task DeleteVaultedCardAsync(
        string vaultToken,
        CancellationToken ct = default);

    Task<IReadOnlyList<PayPalTransactionInfo>> SearchTransactionsAsync(
        DateTimeOffset startDate, DateTimeOffset endDate,
        CancellationToken ct = default);
}
