using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPayPalGateway
{
    string Currency { get; }

    Task<PayPalAuthorizationResult> AuthorizeCardAsync(
        string merchantReference,
        decimal amount,
        CardPaymentDetails card,
        string requestId,
        CancellationToken cancellationToken);

    Task<PayPalAuthorizationResult> AuthorizeVaultedCardAsync(
        string merchantReference,
        decimal amount,
        string vaultId,
        string requestId,
        CancellationToken cancellationToken);

    Task<PayPalAuthorizationDetails> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken);

    Task<PayPalAuthorizationResult> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string requestId,
        CancellationToken cancellationToken);

    Task<PayPalCaptureResult> CaptureAsync(
        string authorizationId,
        decimal amount,
        string requestId,
        CancellationToken cancellationToken);

    Task<PayPalCaptureResult> GetCaptureAsync(string captureId, CancellationToken cancellationToken);

    Task VoidAuthorizationAsync(string authorizationId, string requestId, CancellationToken cancellationToken);

    Task<PayPalRefundResult> RefundAsync(
        string captureId,
        decimal? amount,
        string requestId,
        CancellationToken cancellationToken);

    Task<PayPalVaultedCard> VaultCardAsync(
        CardPaymentDetails card,
        string? paypalCustomerId,
        string requestId,
        CancellationToken cancellationToken);

    Task DeletePaymentTokenAsync(string paymentTokenId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PayPalReportedTransaction>> ListTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);
}
