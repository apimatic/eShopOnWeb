using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public interface IPayPalClient
{
    Task<PayPalOrderResult> CreateOrderWithCardAsync(decimal amount, string currency,
        CardDetails card, string idempotencyKey, CancellationToken ct = default);

    Task<PayPalOrderResult> CreateOrderWithVaultAsync(decimal amount, string currency,
        string vaultId, string idempotencyKey, CancellationToken ct = default);

    Task<PayPalCaptureResult> CaptureAuthorizationAsync(string authorizationId,
        string idempotencyKey, CancellationToken ct = default);

    Task VoidAuthorizationAsync(string authorizationId, CancellationToken ct = default);

    Task<PayPalReauthorizeResult> ReauthorizeAsync(string authorizationId, decimal amount,
        string currency, CancellationToken ct = default);

    Task<PayPalRefundResult> RefundCaptureAsync(string captureId, decimal? amount,
        string currency, string idempotencyKey, CancellationToken ct = default);

    Task<PayPalSetupTokenResult> CreateSetupTokenAsync(CardDetails card,
        string? existingCustomerId, string idempotencyKey, CancellationToken ct = default);

    Task<PayPalVaultTokenResult> CreatePaymentTokenAsync(string setupTokenId,
        string idempotencyKey, CancellationToken ct = default);

    Task<List<PayPalVaultTokenResult>> ListPaymentTokensAsync(string customerId,
        CancellationToken ct = default);

    Task DeletePaymentTokenAsync(string tokenId, CancellationToken ct = default);

    Task<List<PayPalTransactionRecord>> SearchTransactionsAsync(DateTimeOffset startDate,
        DateTimeOffset endDate, CancellationToken ct = default);
}
