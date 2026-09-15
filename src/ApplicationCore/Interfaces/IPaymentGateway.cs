using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPaymentGateway
{
    Task<AuthorizationResult> AuthorizeAsync(AuthorizePaymentRequest request, CancellationToken cancellationToken);
    Task<PaymentAuthorizationSnapshot> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken);
    Task<PaymentAuthorizationSnapshot> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken cancellationToken);
    Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken);
    Task<CaptureResult> CaptureAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken);
    Task<CaptureResult> GetCaptureAsync(string captureId, CancellationToken cancellationToken);
    Task<RefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken cancellationToken);
    Task<VaultedCardResult> VaultCardAsync(CardDetails card, string merchantCustomerId, string idempotencyKey, CancellationToken cancellationToken);
    Task DeleteVaultedCardAsync(string paymentTokenId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProcessorTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}
