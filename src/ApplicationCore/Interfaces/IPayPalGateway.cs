using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the PayPal REST APIs (Orders v2, Payments v2, Vault v3,
/// Transaction Search v1). Implementations must never log full card details.
/// </summary>
public interface IPayPalGateway
{
    Task<string> CreateOrderAsync(decimal amount, string currency, string referenceId,
        string idempotencyKey, CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationResult> AuthorizeOrderAsync(string payPalOrderId,
        PayPalCardDetails? card, string? vaultTokenId,
        string idempotencyKey, CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationDetails> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationDetails> ReauthorizeAsync(string authorizationId,
        decimal amount, string currency, string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<PayPalCaptureResult> CaptureAuthorizationAsync(string authorizationId,
        decimal amount, string currency, string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<PayPalRefundResult> RefundCaptureAsync(string captureId, decimal? amount,
        string currency, string? noteToPayer, string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<PayPalVaultedCard> SaveCardAsync(string customerId, PayPalCardDetails card,
        string idempotencyKey, CancellationToken cancellationToken = default);

    Task DeleteSavedCardAsync(string vaultTokenId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken = default);
}
