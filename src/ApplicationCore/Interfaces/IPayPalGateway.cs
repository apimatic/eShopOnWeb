using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPayPalGateway
{
    Task<AuthorizedPaymentResult> CreateAndAuthorizeWithCardAsync(
        int orderId,
        decimal amount,
        string currency,
        IReadOnlyList<PayPalCheckoutItem> items,
        CardPaymentSource card,
        string invoiceId,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<AuthorizedPaymentResult> CreateAndAuthorizeWithVaultIdAsync(
        int orderId,
        decimal amount,
        string currency,
        IReadOnlyList<PayPalCheckoutItem> items,
        string vaultId,
        string invoiceId,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<AuthorizedPaymentResult> GetAuthorizedOrderAsync(
        string paypalOrderId,
        CancellationToken cancellationToken = default);

    Task<AuthorizationDetails> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default);

    Task<AuthorizationDetails> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<CapturedPaymentResult> CaptureAuthorizationAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string invoiceId,
        string requestId,
        CancellationToken cancellationToken = default);

    Task VoidAuthorizationAsync(
        string authorizationId,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<RefundedPaymentResult> RefundCaptureAsync(
        string captureId,
        decimal? amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<VaultedCardResult> VaultCardAsync(
        CardPaymentSource card,
        string merchantCustomerId,
        string requestId,
        CancellationToken cancellationToken = default);

    Task DeleteVaultedCardAsync(
        string vaultId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PayPalReportedTransaction>> ListTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
