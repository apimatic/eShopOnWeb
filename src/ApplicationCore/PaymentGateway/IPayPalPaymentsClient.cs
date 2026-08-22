using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.PaymentGateway;

public interface IPayPalPaymentsClient
{
    Task<PayPalAuthorizationResult> AuthorizeCardPaymentAsync(
        string invoiceId,
        string customId,
        PayPalMoney amount,
        CardAuthorizationRequest card,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationResult> AuthorizeVaultedCardPaymentAsync(
        string invoiceId,
        string customId,
        PayPalMoney amount,
        string vaultId,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationResult> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationResult> ReauthorizeAsync(
        string authorizationId,
        PayPalMoney amount,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<PayPalCaptureResult> CaptureAuthorizationAsync(
        string authorizationId,
        PayPalMoney amount,
        string requestId,
        CancellationToken cancellationToken = default);

    Task VoidAuthorizationAsync(
        string authorizationId,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<PayPalRefundResult> RefundCaptureAsync(
        string captureId,
        PayPalMoney? amount,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<PayPalVaultedCard> VaultCardAsync(
        CardAuthorizationRequest card,
        string? existingCustomerId,
        string requestId,
        CancellationToken cancellationToken = default);

    Task DeleteVaultedCardAsync(
        string paymentTokenId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PayPalReportedTransaction>> ListTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
