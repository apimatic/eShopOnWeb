using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the payment provider (PayPal). All money movement goes
/// through this seam; implementations must never log full card details.
/// </summary>
public interface IPaymentGateway
{
    Task<PaymentAuthorizationResult> AuthorizeCardAsync(CardDetails card, decimal amount, string currency, string referenceId, string idempotencyKey, CancellationToken ct = default);

    Task<PaymentAuthorizationResult> AuthorizeVaultedCardAsync(string vaultTokenId, decimal amount, string currency, string referenceId, string idempotencyKey, CancellationToken ct = default);

    Task<AuthorizationState> GetAuthorizationAsync(string authorizationId, CancellationToken ct = default);

    Task<AuthorizationState> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken ct = default);

    Task<CaptureResult> CaptureAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default);

    Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default);

    Task<RefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken ct = default);

    Task<VaultedCardResult> VaultCardAsync(CardDetails card, string customerId, string idempotencyKey, CancellationToken ct = default);

    Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken ct = default);

    Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
