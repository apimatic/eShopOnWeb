using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPaymentService
{
    Task<PaymentAuthResult> AuthorizePaymentAsync(
        decimal amount,
        string currency,
        CardDetails? card,
        string? vaultToken,
        string eShopOrderId,
        CancellationToken ct = default);

    Task<CaptureResult> CapturePaymentAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string eShopOrderId,
        CancellationToken ct = default);

    Task<string> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        CancellationToken ct = default);

    Task VoidAuthorizationAsync(string authorizationId, CancellationToken ct = default);

    Task<string> RefundPaymentAsync(
        string captureId,
        decimal? amount,
        string currency,
        string idempotencyKey,
        CancellationToken ct = default);

    Task<SavedCardInfo> SaveCardAsync(
        string customerId,
        CardDetails card,
        CancellationToken ct = default);

    Task<IReadOnlyList<SavedCardInfo>> ListSavedCardsAsync(
        string customerId,
        CancellationToken ct = default);

    Task DeleteSavedCardAsync(string vaultToken, CancellationToken ct = default);

    Task<IReadOnlyList<TransactionRecord>> SearchTransactionsAsync(
        string startDate,
        string endDate,
        CancellationToken ct = default);
}

public record CardDetails(string Number, string Expiry, string? Cvv, string? Name);

public record PaymentAuthResult(
    string PayPalOrderId,
    string AuthorizationId,
    DateTimeOffset? AuthorizationExpiry,
    DateTimeOffset? AuthorizationCreatedAt);

public record CaptureResult(
    string CaptureId,
    decimal CapturedAmount,
    decimal PayPalFee,
    decimal NetAmount);

public record SavedCardInfo(
    string VaultToken,
    string? Last4,
    string? CardBrand,
    string? ExpiryMonth,
    string? ExpiryYear);

public record TransactionRecord(
    string? TransactionId,
    string? Amount,
    string? Fee,
    string? Status,
    string? CreateTime,
    string? PayPalReference);
