using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPaymentGateway
{
    Task<string> CreateOrderWithCardAsync(
        int orderId,
        decimal amount,
        string currency,
        CardPaymentDetails card,
        string requestId,
        CancellationToken ct);

    Task<string> CreateOrderWithVaultIdAsync(
        int orderId,
        decimal amount,
        string currency,
        string vaultId,
        string requestId,
        CancellationToken ct);

    Task<AuthorizationResult> AuthorizeExistingOrderAsync(
        string payPalOrderId,
        string requestId,
        CancellationToken ct);

    Task<AuthorizationResult> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string requestId,
        CancellationToken ct);

    Task<CaptureResult> CaptureAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string invoiceId,
        string requestId,
        CancellationToken ct);

    Task<CaptureResult> GetCaptureAsync(string captureId, CancellationToken ct);

    Task VoidAsync(string authorizationId, string requestId, CancellationToken ct);

    Task<RefundGatewayResult> RefundAsync(
        string captureId,
        decimal amount,
        string currency,
        string requestId,
        CancellationToken ct);

    Task<VaultedCardResult> VaultCardAsync(
        string merchantCustomerId,
        CardPaymentDetails card,
        string requestId,
        CancellationToken ct);

    Task<IReadOnlyList<VaultedCardResult>> ListVaultedCardsAsync(
        string merchantCustomerId,
        string? payPalCustomerId,
        CancellationToken ct);

    Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct);

    Task<IReadOnlyList<PayPalTransactionRecord>> SearchTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct);
}
