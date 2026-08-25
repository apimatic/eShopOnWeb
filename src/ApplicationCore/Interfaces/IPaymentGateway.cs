using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the payment provider (PayPal). ApplicationCore depends only on this
/// interface and the plain DTOs in <see cref="ApplicationCore.PaymentProcessing"/> — no
/// provider SDK type crosses this boundary. <paramref name="requestId"/>/<paramref name="idempotencyKey"/>
/// parameters are forwarded to the provider as an idempotency key so a retried call has no
/// duplicate effect.
/// </summary>
public interface IPaymentGateway
{
    Task<CardAuthorizationResult> AuthorizeWithCardAsync(CardDetails card, decimal amount, string currency, string requestId, CancellationToken ct);

    Task<CardAuthorizationResult> AuthorizeWithVaultedCardAsync(string vaultId, decimal amount, string currency, string requestId, CancellationToken ct);

    Task<SaveCardResult> SaveCardAsync(CardDetails card, string requestId, CancellationToken ct);

    Task DeleteSavedCardAsync(string vaultId, CancellationToken ct);

    Task<CaptureResult> CaptureAsync(string authorizationId, string requestId, CancellationToken ct);

    Task<ReauthorizeResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string requestId, CancellationToken ct);

    Task VoidAsync(string authorizationId, string requestId, CancellationToken ct);

    Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken ct);

    /// <summary>Returns every transaction PayPal recorded in the range, across all pages.</summary>
    Task<IReadOnlyList<TransactionRecord>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
