using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

/// <summary>
/// Abstraction over the payment provider (PayPal). ApplicationCore depends only on this
/// interface and the plain DTOs it exchanges - no provider SDK type crosses this boundary.
/// </summary>
public interface IPaymentGatewayService
{
    Task<PaymentAuthorizationResult> AuthorizeWithCardAsync(
        PaymentAmount amount, CardDetails card, string requestId, CancellationToken ct);

    Task<PaymentAuthorizationResult> AuthorizeWithVaultedCardAsync(
        PaymentAmount amount, string vaultId, string requestId, CancellationToken ct);

    Task<PaymentAuthorizationStatusResult> GetAuthorizationAsync(string authorizationId, CancellationToken ct);

    Task<PaymentCaptureResult> CaptureAuthorizationAsync(string authorizationId, string requestId, CancellationToken ct);

    Task<PaymentAuthorizationStatusResult> ReauthorizeAsync(
        string authorizationId, PaymentAmount amount, string requestId, CancellationToken ct);

    Task VoidAuthorizationAsync(string authorizationId, string requestId, CancellationToken ct);

    Task<PaymentRefundResult> RefundCaptureAsync(
        string captureId, PaymentAmount? amount, string idempotencyKey, CancellationToken ct);

    Task<VaultedCardResult> SaveCardAsync(string customerId, CardDetails card, string requestId, CancellationToken ct);

    Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct);

    Task<TransactionSearchResult> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
