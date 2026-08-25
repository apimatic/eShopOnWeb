using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the PayPal payment gateway. Kept in ApplicationCore so the domain/application
/// layer never references the PayPal SDK directly - the SDK-backed implementation lives in
/// Infrastructure. All failures surface as <see cref="Exceptions.PayPalGatewayException"/>.
/// </summary>
public interface IPayPalGateway
{
    Task<VaultedCardResult> CreatePaymentTokenAsync(CardDetails card, string merchantCustomerId, CancellationToken ct);

    Task DeletePaymentTokenAsync(string vaultId, CancellationToken ct);

    Task<OrderAuthorizationResult> AuthorizeAsync(decimal amount, string currency, string payPalRequestId, CardDetails? card, string? vaultId, CancellationToken ct);

    Task<ReauthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string payPalRequestId, CancellationToken ct);

    Task<CaptureResult> CaptureAsync(string authorizationId, decimal amount, string currency, string payPalRequestId, CancellationToken ct);

    Task VoidAsync(string authorizationId, string payPalRequestId, CancellationToken ct);

    Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken ct);

    Task<IReadOnlyList<PayPalTransactionRecord>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
