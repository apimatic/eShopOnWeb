using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record PayPalCardSource(
    string Number,
    string Expiry,
    string SecurityCode,
    string? Name,
    string? AddressLine1,
    string? City,
    string? State,
    string? CountryCode,
    string? PostalCode);

public record PayPalAuthResult(string PayPalOrderId, string AuthorizationId);
public record PayPalCaptureResult(string CaptureId, decimal GrossAmount, decimal FeeAmount, decimal NetAmount);
public record PayPalRefundResult(string RefundId, decimal Amount);
public record PayPalVaultResult(string VaultTokenId, string? LastFourDigits, string? CardBrand, string? Expiry, string? CardType);

public record PayPalTransaction(
    string? TransactionId,
    string? ReferenceId,
    string? Status,
    decimal? Amount,
    decimal? FeeAmount,
    string? InitiationDate);

public interface IPayPalService
{
    Task<PayPalAuthResult> AuthorizeWithCardAsync(
        decimal amount, string currency, string idempotencyBase,
        PayPalCardSource card, CancellationToken ct = default);

    Task<PayPalAuthResult> AuthorizeWithVaultedCardAsync(
        decimal amount, string currency, string idempotencyBase,
        string vaultTokenId, CancellationToken ct = default);

    Task<(bool isExpired, bool isVoidedOrDenied)> GetAuthorizationStatusAsync(
        string authorizationId, CancellationToken ct = default);

    Task<string> ReauthorizeAsync(
        string authorizationId, decimal amount, string currency, CancellationToken ct = default);

    Task<PayPalCaptureResult> CaptureAuthorizationAsync(
        string authorizationId, string captureIdempotencyKey, CancellationToken ct = default);

    Task VoidAuthorizationAsync(string authorizationId, CancellationToken ct = default);

    Task<PayPalRefundResult> RefundAsync(
        string captureId, decimal? amount, string currency,
        string idempotencyKey, CancellationToken ct = default);

    Task<PayPalVaultResult> VaultCardAsync(
        string customerId, PayPalCardSource card,
        string idempotencyKey, CancellationToken ct = default);

    Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken ct = default);

    Task<IReadOnlyList<PayPalTransaction>> GetTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
