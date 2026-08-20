using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPayPalGateway
{
    Task<PayPalAuthorizationResult> AuthorizeCardAsync(
        int orderId,
        string invoiceId,
        string amountValue,
        string currency,
        PayPalCardInput card,
        string payPalRequestId,
        CancellationToken cancellationToken);

    Task<PayPalAuthorizationResult> AuthorizeSavedCardAsync(
        int orderId,
        string invoiceId,
        string amountValue,
        string currency,
        string vaultId,
        string payPalRequestId,
        CancellationToken cancellationToken);

    Task<PayPalAuthorizationSnapshot> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken);

    Task<PayPalAuthorizationSnapshot> ReauthorizeAsync(
        string authorizationId,
        string amountValue,
        string currency,
        string payPalRequestId,
        CancellationToken cancellationToken);

    Task<PayPalCaptureResult> CaptureAsync(
        string authorizationId,
        int orderId,
        string payPalRequestId,
        CancellationToken cancellationToken);

    Task<PayPalCaptureResult> GetCaptureAsync(
        string captureId,
        CancellationToken cancellationToken);

    Task<PayPalAuthorizationSnapshot> VoidAsync(
        string authorizationId,
        string payPalRequestId,
        CancellationToken cancellationToken);

    Task<PayPalRefundResult> RefundAsync(
        string captureId,
        string amountValue,
        string currency,
        string payPalRequestId,
        bool fullRefund,
        CancellationToken cancellationToken);

    Task<PayPalVaultedCard> VaultCardAsync(
        string merchantCustomerId,
        PayPalCardInput card,
        string payPalRequestId,
        CancellationToken cancellationToken);

    Task DeleteVaultedCardAsync(string paymentTokenId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PayPalTransactionRecord>> SearchTransactionsAsync(
        string startDate,
        string endDate,
        CancellationToken cancellationToken);
}
