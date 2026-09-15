using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPayPalPaymentsClient
{
    string Currency { get; }

    Task<string> CreateAuthorizeOrderAsync(int orderId, decimal amount, string invoiceId, CancellationToken cancellationToken = default);

    Task<PaypalAuthorizationResult> AuthorizeOrderAsync(
        string paypalOrderId,
        string invoiceId,
        CardPaymentInput? card,
        string? vaultId,
        CancellationToken cancellationToken = default);

    Task<PaypalAuthorizationDetails> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    Task<PaypalAuthorizationDetails> ReauthorizeAsync(string authorizationId, decimal amount, CancellationToken cancellationToken = default);

    Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    Task<PaypalCaptureResult> CaptureAuthorizationAsync(string authorizationId, string invoiceId, decimal amount, CancellationToken cancellationToken = default);

    Task<PaypalRefundResult> RefundCaptureAsync(string captureId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default);

    Task<PaypalVaultedCard> VaultCardAsync(CardPaymentInput card, string? paypalCustomerId, CancellationToken cancellationToken = default);

    Task DeletePaymentTokenAsync(string paymentTokenId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PaypalReportedTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
