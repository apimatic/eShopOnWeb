using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPayPalClient
{
    string Currency { get; }

    Task<PayPalOrderResult> CreateOrderAsync(
        decimal amount,
        string customId,
        string invoiceId,
        CardDetails? card,
        string? vaultId,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<PayPalOrderResult> AuthorizeOrderAsync(
        string paypalOrderId,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<PayPalOrderResult> GetOrderAsync(
        string paypalOrderId,
        CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationResult> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationResult> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string requestId,
        CancellationToken cancellationToken = default);

    Task VoidAuthorizationAsync(
        string authorizationId,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<PayPalCaptureResult> CaptureAuthorizationAsync(
        string authorizationId,
        decimal amount,
        string invoiceId,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<PayPalRefundResult> RefundCaptureAsync(
        string captureId,
        decimal? amount,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<VaultedCardResult> VaultCardAsync(
        CardDetails card,
        string customerId,
        string requestId,
        CancellationToken cancellationToken = default);

    Task DeletePaymentTokenAsync(
        string vaultId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PayPalReportedTransaction>> ListAllTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
