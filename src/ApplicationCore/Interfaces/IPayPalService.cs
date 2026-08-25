using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record DirectCardDetails(
    string Number,
    string Expiry,
    string SecurityCode,
    string? Name,
    string CountryCode,
    string? AddressLine1,
    string? City,
    string? State,
    string? PostalCode);

public record AuthorizeResult(
    string PayPalOrderId,
    string AuthorizationId,
    DateTimeOffset? AuthorizationExpiry);

public record CaptureResult(
    string CaptureId,
    decimal CapturedAmount,
    decimal PayPalFee,
    decimal NetAmount);

public record RefundResult(
    string RefundId,
    decimal Amount,
    string Currency);

public record ReauthorizeResult(
    string NewAuthorizationId,
    DateTimeOffset? NewExpiry);

public record VaultTokenResult(
    string TokenId,
    string? PayPalCustomerId,
    string? Last4,
    string? Brand,
    string? Expiry);

public record TransactionRecord(
    string? TransactionId,
    string? Amount,
    string? Currency,
    string? Status,
    string? InitiatedDate,
    string? InvoiceId,
    string? CustomField,
    string? ReferenceId);

public interface IPayPalService
{
    Task<AuthorizeResult> CreateAndAuthorizeAsync(
        decimal amount,
        string currency,
        string eShopOrderId,
        string idempotencyKey,
        DirectCardDetails card,
        CancellationToken ct = default);

    Task<AuthorizeResult> CreateAndAuthorizeWithVaultAsync(
        decimal amount,
        string currency,
        string eShopOrderId,
        string idempotencyKey,
        string vaultTokenId,
        CancellationToken ct = default);

    Task<CaptureResult> CaptureAsync(
        string authorizationId,
        string idempotencyKey,
        CancellationToken ct = default);

    Task VoidAsync(
        string authorizationId,
        CancellationToken ct = default);

    Task<RefundResult> RefundAsync(
        string captureId,
        decimal? amount,
        string currency,
        string idempotencyKey,
        CancellationToken ct = default);

    Task<ReauthorizeResult> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string idempotencyKey,
        CancellationToken ct = default);

    Task<VaultTokenResult> VaultCardAsync(
        string idempotencyKey,
        DirectCardDetails card,
        string? existingPayPalCustomerId,
        string merchantCustomerId,
        CancellationToken ct = default);

    Task<IReadOnlyList<VaultTokenResult>> ListVaultedCardsAsync(
        string paypalCustomerId,
        CancellationToken ct = default);

    Task DeleteVaultedCardAsync(
        string tokenId,
        CancellationToken ct = default);

    Task<IReadOnlyList<TransactionRecord>> GetTransactionsAsync(
        string startDate,
        string endDate,
        CancellationToken ct = default);
}
