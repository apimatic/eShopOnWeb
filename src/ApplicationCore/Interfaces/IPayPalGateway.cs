using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPayPalGateway
{
    Task<AuthorizationResult> AuthorizeCardAsync(
        int orderId,
        decimal amount,
        string currency,
        string payPalRequestId,
        CardPaymentInput card,
        CancellationToken ct);

    Task<AuthorizationResult> AuthorizeSavedCardAsync(
        int orderId,
        decimal amount,
        string currency,
        string payPalRequestId,
        string vaultId,
        CancellationToken ct);

    Task<AuthorizationResult> GetAuthorizationAsync(string authorizationId, CancellationToken ct);

    Task<AuthorizationResult> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string payPalRequestId,
        CancellationToken ct);

    Task<CaptureResult> CaptureAsync(string authorizationId, string payPalRequestId, CancellationToken ct);

    Task<CaptureResult> GetCaptureAsync(string captureId, CancellationToken ct);

    Task VoidAsync(string authorizationId, string payPalRequestId, CancellationToken ct);

    Task<RefundResult> RefundAsync(
        string captureId,
        decimal? amount,
        string currency,
        string payPalRequestId,
        CancellationToken ct);

    Task<VaultedCardResult> SaveCardAsync(string merchantCustomerId, CardPaymentInput card, string payPalRequestId, CancellationToken ct);

    Task DeletePaymentTokenAsync(string paymentTokenId, CancellationToken ct);

    Task<IReadOnlyList<PayPalTransactionRecord>> SearchTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct);
}
