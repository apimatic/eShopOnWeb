using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPayPalClient
{
    Task<PayPalOrderSnapshot> CreateAuthorizeOrderAsync(
        CreatePayPalAuthorizeRequest request,
        string paypalRequestId,
        CancellationToken cancellationToken = default);

    Task<PayPalOrderSnapshot> AuthorizeOrderAsync(
        string paypalOrderId,
        string paypalRequestId,
        CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationSnapshot> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationSnapshot> ReauthorizeAsync(
        string authorizationId,
        string currencyCode,
        string amountValue,
        string paypalRequestId,
        CancellationToken cancellationToken = default);

    Task<PayPalCaptureSnapshot> CaptureAuthorizationAsync(
        string authorizationId,
        string paypalRequestId,
        CancellationToken cancellationToken = default);

    Task<PayPalCaptureSnapshot> GetCaptureAsync(
        string captureId,
        CancellationToken cancellationToken = default);

    Task VoidAuthorizationAsync(
        string authorizationId,
        string paypalRequestId,
        CancellationToken cancellationToken = default);

    Task<PayPalRefundSnapshot> RefundCaptureAsync(
        string captureId,
        string? currencyCode,
        string? amountValue,
        string paypalRequestId,
        CancellationToken cancellationToken = default);

    Task<PayPalVaultedCard> CreatePaymentTokenAsync(
        CardPaymentSource card,
        string merchantCustomerId,
        string? paypalCustomerId,
        string paypalRequestId,
        CancellationToken cancellationToken = default);

    Task DeletePaymentTokenAsync(
        string paymentTokenId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PayPalReportedTransaction>> ListTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
