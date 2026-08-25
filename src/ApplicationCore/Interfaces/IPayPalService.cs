using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPayPalService
{
    Task<PayPalAuthorizeResult> AuthorizeWithCardAsync(
        decimal amount, string currency, PayPalCardDetails card, string idempotencyKey, CancellationToken ct = default);

    Task<PayPalAuthorizeResult> AuthorizeWithVaultTokenAsync(
        decimal amount, string currency, string paymentTokenId, string idempotencyKey, CancellationToken ct = default);

    Task<PayPalCaptureResult> CaptureAuthorizationAsync(
        string authorizationId, string idempotencyKey, CancellationToken ct = default);

    Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default);

    Task<PayPalRefundResult> RefundCaptureAsync(
        string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken ct = default);

    Task<PayPalVaultResult> VaultCardAsync(
        string merchantCustomerId, PayPalCardDetails card, CancellationToken ct = default);

    Task DeleteVaultTokenAsync(string paymentTokenId, CancellationToken ct = default);

    Task<IReadOnlyList<PayPalTransactionRecord>> GetTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
