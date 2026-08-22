using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payment;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPayPalGateway
{
    Task<PayPalOrderResult> CreateAuthorizedOrderAsync(CreateAuthorizedOrderCommand command, CancellationToken cancellationToken = default);
    Task<PayPalOrderResult> GetOrderAsync(string payPalOrderId, CancellationToken cancellationToken = default);
    Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);
    Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string requestId, CancellationToken cancellationToken = default);
    Task<PayPalCaptureResult> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currency, string requestId, CancellationToken cancellationToken = default);
    Task VoidAuthorizationAsync(string authorizationId, string requestId, CancellationToken cancellationToken = default);
    Task<PayPalRefundResult> RefundCaptureAsync(string captureId, decimal amount, string currency, string requestId, CancellationToken cancellationToken = default);
    Task<PayPalVaultedCardResult> VaultCardAsync(CardPaymentSource card, string merchantCustomerId, string? payPalCustomerId, string requestId, CancellationToken cancellationToken = default);
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PayPalReportedTransaction>> SearchAllTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
