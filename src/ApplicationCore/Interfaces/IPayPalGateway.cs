using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPayPalGateway
{
    Task<PayPalAuthorizationResult> AuthorizeCardAsync(
        decimal amount,
        string currency,
        string customId,
        string invoiceId,
        IReadOnlyList<PayPalPurchaseLine> items,
        CardPaymentSource card,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationResult> AuthorizeVaultedCardAsync(
        decimal amount,
        string currency,
        string customId,
        string invoiceId,
        IReadOnlyList<PayPalPurchaseLine> items,
        string vaultId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationDetails> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationDetails> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<PayPalCaptureResult> CaptureAuthorizationAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string invoiceId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task VoidAuthorizationAsync(
        string authorizationId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<PayPalRefundResult> RefundCaptureAsync(
        string captureId,
        decimal amount,
        string currency,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<PayPalVaultedCard> VaultCardAsync(
        CardPaymentSource card,
        string merchantCustomerId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task DeleteVaultedCardAsync(
        string paymentTokenId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PayPalReportedTransaction>> ListAllTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
