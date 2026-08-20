using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPayPalGateway
{
    Task<PayPalAuthorizationResult> AuthorizeCardAsync(
        decimal amount,
        string currency,
        string invoiceId,
        string customId,
        string requestId,
        CardDetails card,
        CancellationToken cancellationToken);

    Task<PayPalAuthorizationResult> AuthorizeVaultedCardAsync(
        decimal amount,
        string currency,
        string invoiceId,
        string customId,
        string requestId,
        string vaultId,
        CancellationToken cancellationToken);

    Task<PayPalAuthorizationDetails> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken);

    Task<PayPalAuthorizationResult> ReauthorizeAsync(
        string authorizationId,
        string paypalOrderId,
        decimal amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken);

    Task<PayPalCaptureResult> CaptureAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string invoiceId,
        string requestId,
        CancellationToken cancellationToken);

    Task VoidAuthorizationAsync(string authorizationId, string requestId, CancellationToken cancellationToken);

    Task<PayPalRefundResult> RefundAsync(
        string captureId,
        decimal? amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken);

    Task<PayPalVaultedCardResult> VaultCardAsync(
        CardDetails card,
        string merchantCustomerId,
        string? paypalCustomerId,
        string requestId,
        CancellationToken cancellationToken);

    Task DeleteVaultedCardAsync(string paymentTokenId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PayPalReportedTransaction>> ListAllTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);
}

public interface IPaymentSettings
{
    string Currency { get; }
}

public interface IOrderOperationLock
{
    Task<IAsyncDisposable> AcquireAsync(int orderId, CancellationToken cancellationToken);
}
