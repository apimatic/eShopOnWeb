using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPayPalGateway
{
    string Currency { get; }

    Task<AuthorizationResult> AuthorizeAsync(AuthorizePaymentCommand command, CancellationToken cancellationToken = default);

    Task<AuthorizationSnapshot> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    Task<AuthorizationSnapshot> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default);

    Task<CaptureResult> CaptureAsync(string authorizationId, decimal amount, string currency, string invoiceId, string idempotencyKey, CancellationToken cancellationToken = default);

    Task<CaptureResult> GetCaptureAsync(string captureId, CancellationToken cancellationToken = default);

    Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default);

    Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default);

    Task<VaultedCardResult> VaultCardAsync(CardPaymentDetails card, string merchantCustomerId, string idempotencyKey, CancellationToken cancellationToken = default);

    Task DeletePaymentTokenAsync(string paymentTokenId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PayPalReportedTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
