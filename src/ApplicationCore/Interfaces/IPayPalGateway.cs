using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Confirmed against PayPal Orders v2, Payments v2, Vault v3, and Transaction Search v1.
/// </summary>
public interface IPayPalGateway
{
    Task<PayPalAuthorizationResult> AuthorizeCardPaymentAsync(
        decimal amount,
        string currency,
        string invoiceId,
        CardPaymentDetails card,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationResult> AuthorizeVaultedCardPaymentAsync(
        decimal amount,
        string currency,
        string invoiceId,
        string vaultId,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationDetails> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationResult> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<PayPalCaptureResult> CaptureAuthorizationAsync(
        string authorizationId,
        string requestId,
        CancellationToken cancellationToken = default);

    Task VoidAuthorizationAsync(
        string authorizationId,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<PayPalRefundResult> RefundCaptureAsync(
        string captureId,
        decimal amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<PayPalVaultedCardResult> VaultCardAsync(
        CardPaymentDetails card,
        string? paypalCustomerId,
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
