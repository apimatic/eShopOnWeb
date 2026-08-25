using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPayPalPaymentService
{
    Task<AuthorizeResult> AuthorizeAsync(
        int orderId,
        decimal amount,
        CardDetails? card,
        string? savedCardTokenId,
        string idempotencyKey,
        CancellationToken ct = default);

    Task CaptureAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string idempotencyKey,
        CancellationToken ct = default);

    Task<CaptureResult> CaptureWithBreakdownAsync(
        string authorizationId,
        string idempotencyKey,
        CancellationToken ct = default);

    Task<RenewAuthResult> RenewAuthorizationIfNeededAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string idempotencyKey,
        CancellationToken ct = default);

    Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default);

    Task<RefundResult> RefundAsync(
        string captureId,
        decimal? partialAmount,
        string currency,
        string idempotencyKey,
        decimal capturedAmount = 0m,
        CancellationToken ct = default);

    Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct = default);

    Task<SavedCardResult> SaveCardAsync(
        string merchantCustomerId,
        CardDetails card,
        string idempotencyKey,
        CancellationToken ct = default);

    Task<IReadOnlyList<SavedCardInfo>> ListSavedCardsAsync(
        string merchantCustomerId,
        CancellationToken ct = default);

    Task DeleteSavedCardAsync(string paymentTokenId, CancellationToken ct = default);
}

public record CardDetails(
    string Number,
    string Expiry,
    string SecurityCode,
    string? Name = null,
    string? BillingCountryCode = null);

public record AuthorizeResult(
    string PayPalOrderId,
    string AuthorizationId);

public record CaptureResult(
    string CaptureId,
    decimal CapturedAmount,
    decimal? PayPalFee,
    decimal? NetAmount);

public record RenewAuthResult(
    bool Renewed,
    string AuthorizationId,
    string? OperatorMessage = null);

public record RefundResult(string RefundId, decimal Amount);

public record PayPalTransaction(
    string TransactionId,
    string? ReferenceId,
    decimal? Amount,
    string? Status,
    DateTimeOffset? InitiatedAt,
    string? InvoiceId);

public record SavedCardResult(
    string PaymentTokenId,
    string? LastFourDigits,
    string? CardBrand,
    string? Expiry);

public record SavedCardInfo(
    string PaymentTokenId,
    string? LastFourDigits,
    string? CardBrand,
    string? Expiry);
