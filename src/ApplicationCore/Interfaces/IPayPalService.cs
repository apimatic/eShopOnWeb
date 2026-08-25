using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record CardDetails(
    string CardNumber,
    int ExpiryYear,
    int ExpiryMonth,
    string Cvv,
    string CardholderName,
    string? Street,
    string? City,
    string? State,
    string? Country,
    string? ZipCode);

public record AuthorizeResult(string PayPalOrderId, string AuthorizationId);

public record CaptureResult(
    string CaptureId,
    decimal CapturedAmount,
    decimal PayPalFee,
    decimal NetAmount);

public record RefundResult(string RefundId, decimal Amount);

public record VaultResult(
    string VaultId,
    string PayPalCustomerId,
    string Last4,
    string Brand,
    int ExpiryYear,
    int ExpiryMonth);

public record PayPalTransaction(
    string TransactionId,
    string? PayPalReferenceId,
    string? Status,
    decimal Amount,
    string Currency,
    string? CustomField,
    string? EventCode,
    DateTimeOffset? InitiationDate);

public interface IPayPalService
{
    Task<AuthorizeResult> AuthorizeWithCardAsync(
        string idempotencyKey, decimal amount, string currency,
        CardDetails card, string merchantCustomerId, CancellationToken ct = default);

    Task<AuthorizeResult> AuthorizeWithVaultAsync(
        string idempotencyKey, decimal amount, string currency,
        string vaultId, CancellationToken ct = default);

    Task<(string Status, DateTimeOffset? ExpirationTime)> GetAuthorizationAsync(
        string authorizationId, CancellationToken ct = default);

    Task<CaptureResult> CaptureAsync(
        string authorizationId, string idempotencyKey, CancellationToken ct = default);

    Task<string> ReauthorizeAsync(
        string authorizationId, CancellationToken ct = default);

    Task VoidAsync(
        string authorizationId, CancellationToken ct = default);

    Task<RefundResult> RefundAsync(
        string captureId, decimal? amount, string currency,
        string idempotencyKey, string? note, CancellationToken ct = default);

    Task<VaultResult> VaultCardAsync(
        string merchantCustomerId, CardDetails card, CancellationToken ct = default);

    Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct = default);

    Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
