using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPayPalGateway
{
    string Currency { get; }

    Task<string> CreateAuthorizedOrderAsync(
        decimal amount,
        string invoiceId,
        string customId,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<AuthorizedPaymentResult> AuthorizeCardAsync(
        string payPalOrderId,
        CardPaymentSource card,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<AuthorizedPaymentResult> AuthorizeVaultedCardAsync(
        string payPalOrderId,
        VaultedCardPaymentSource vaultedCard,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<AuthorizationDetails> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default);

    Task<AuthorizationDetails> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<CapturedPaymentResult> CaptureAuthorizationAsync(
        string authorizationId,
        decimal amount,
        string invoiceId,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<CapturedPaymentResult> GetCaptureAsync(
        string captureId,
        CancellationToken cancellationToken = default);

    Task VoidAuthorizationAsync(
        string authorizationId,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<RefundPaymentResult> RefundCaptureAsync(
        string captureId,
        decimal? amount,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<VaultedCardResult> VaultCardAsync(
        CardPaymentSource card,
        string merchantCustomerId,
        string? payPalCustomerId,
        string requestId,
        CancellationToken cancellationToken = default);

    Task DeletePaymentTokenAsync(
        string paymentTokenId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PayPalReportedTransaction>> ListTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
