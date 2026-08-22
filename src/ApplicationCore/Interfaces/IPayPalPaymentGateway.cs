using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payment;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPayPalPaymentGateway
{
    Task<AuthorizationHold> AuthorizeCardAsync(
        int orderId,
        string invoiceId,
        decimal amount,
        string currency,
        CardPaymentInput card,
        string payPalRequestId,
        CancellationToken cancellationToken);

    Task<AuthorizationHold> AuthorizeVaultedCardAsync(
        int orderId,
        string invoiceId,
        decimal amount,
        string currency,
        string vaultId,
        string payPalRequestId,
        CancellationToken cancellationToken);

    Task<CaptureProceeds> CaptureAsync(
        string authorizationId,
        string invoiceId,
        string payPalRequestId,
        CancellationToken cancellationToken);

    Task<AuthorizationHold> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string payPalRequestId,
        CancellationToken cancellationToken);

    Task<string> VoidAsync(
        string authorizationId,
        string payPalRequestId,
        CancellationToken cancellationToken);

    Task<RefundProceeds> RefundAsync(
        string captureId,
        decimal? amount,
        string currency,
        string payPalRequestId,
        CancellationToken cancellationToken);

    Task<VaultedCard> SaveCardAsync(
        string merchantCustomerId,
        CardPaymentInput card,
        string payPalRequestId,
        CancellationToken cancellationToken);

    Task DeleteSavedCardAsync(string payPalPaymentTokenId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PayPalReportedTransaction>> SearchTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        string currency,
        CancellationToken cancellationToken);
}
