using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPayPalPaymentGateway
{
    Task<PayPalAuthorizationResult> AuthorizeCardAsync(
        int orderId,
        decimal amount,
        string currency,
        string payPalRequestId,
        CardPaymentDetails card,
        CancellationToken cancellationToken);

    Task<PayPalAuthorizationResult> AuthorizeVaultedCardAsync(
        int orderId,
        decimal amount,
        string currency,
        string payPalRequestId,
        string vaultId,
        CancellationToken cancellationToken);

    Task<PayPalAuthorizationDetails> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken);

    Task<PayPalAuthorizationDetails> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string payPalRequestId,
        CancellationToken cancellationToken);

    Task<PayPalCaptureResult> CaptureAsync(
        string authorizationId,
        string payPalRequestId,
        CancellationToken cancellationToken);

    Task<PayPalCaptureResult> GetCaptureAsync(
        string captureId,
        CancellationToken cancellationToken);

    Task VoidAsync(
        string authorizationId,
        string payPalRequestId,
        CancellationToken cancellationToken);

    Task<PayPalRefundResult> RefundAsync(
        string captureId,
        decimal? amount,
        string? currency,
        string payPalRequestId,
        CancellationToken cancellationToken);

    Task<PayPalVaultedCard> SaveCardAsync(
        string merchantCustomerId,
        string? payPalCustomerId,
        string payPalRequestId,
        CardPaymentDetails card,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PayPalVaultedCard>> ListCardsAsync(
        string customerId,
        CancellationToken cancellationToken);

    Task DeleteCardAsync(string paymentTokenId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PayPalTransactionRecord>> SearchTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);
}
