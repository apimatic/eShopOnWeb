using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPayPalPaymentService
{
    Task<AuthorizeResult> AuthorizeAsync(int orderId, decimal amount, string currency, CardDetails card, CancellationToken ct = default);
    Task<AuthorizeResult> AuthorizeWithVaultAsync(int orderId, decimal amount, string currency, string vaultId, CancellationToken ct = default);
    Task<CaptureResult> CaptureAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default);
    Task<string> ReauthorizeAsync(string authorizationId, decimal amount, string currency, CancellationToken ct = default);
    Task VoidAsync(string authorizationId, CancellationToken ct = default);
    Task<RefundResult> RefundAsync(string captureId, string idempotencyKey, decimal? amount, string currency, CancellationToken ct = default);
    Task<VaultResult> VaultCardAsync(string merchantCustomerId, CardDetails card, CancellationToken ct = default);
    Task<IReadOnlyList<VaultedCard>> ListVaultedCardsAsync(string merchantCustomerId, CancellationToken ct = default);
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct = default);
    Task<IReadOnlyList<TransactionRecord>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}

public record CardDetails(
    string Number,
    string Expiry,
    string SecurityCode,
    string? Name,
    string? CountryCode);

public record AuthorizeResult(
    string PayPalOrderId,
    string AuthorizationId,
    DateTimeOffset? ExpiresAt);

public record CaptureResult(
    string CaptureId,
    decimal GrossAmount,
    decimal? PayPalFee,
    decimal? NetAmount);

public record RefundResult(
    string RefundId,
    decimal RefundedAmount);

public record VaultResult(
    string VaultId,
    string? LastFour,
    string? Brand,
    string? Expiry,
    string? Name);

public record VaultedCard(
    string VaultId,
    string? LastFour,
    string? Brand,
    string? Expiry,
    string? Name);

public record TransactionRecord(
    string? TransactionId,
    string? Status,
    decimal? Amount,
    string? Currency,
    decimal? Fee,
    string? CustomField,
    string? InitiationDate);
