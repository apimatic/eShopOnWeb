using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public interface IPayPalGateway
{
    string Currency { get; }

    Task<PaymentHold> AuthorizeCardPaymentAsync(
        int orderId,
        decimal amount,
        CardPaymentDetails card,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<PaymentHold> AuthorizeVaultedCardPaymentAsync(
        int orderId,
        decimal amount,
        string vaultId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<PaymentAuthorizationDetails> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default);

    Task<PaymentAuthorizationDetails> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<PaymentCapture> CaptureAuthorizationAsync(
        string authorizationId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task VoidAuthorizationAsync(
        string authorizationId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<PaymentRefund> RefundCaptureAsync(
        string captureId,
        decimal amount,
        string currency,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<VaultedCard> VaultCardAsync(
        string customerId,
        CardPaymentDetails card,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task DeleteVaultedCardAsync(
        string vaultId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PayPalReportedTransaction>> ListTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
