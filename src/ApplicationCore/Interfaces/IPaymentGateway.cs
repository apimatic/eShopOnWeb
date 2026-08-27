using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Card details for a one-off payment or for vaulting. Full card data passes
/// through to PayPal only — it is never persisted and never logged.
/// </summary>
public record CardDetails(
    string Number,
    string Expiry,
    string? SecurityCode,
    string? Name,
    string? AddressLine1,
    string? City,
    string? State,
    string? PostalCode,
    string? CountryCode);

public record AuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

public record AuthorizationState(string Status, DateTimeOffset? ExpiresAt);

public record CaptureResult(
    string CaptureId,
    string Status,
    decimal Amount,
    decimal? Fee,
    decimal? Net);

public record RefundResult(string RefundId, string Status, decimal Amount);

public record VaultedCardResult(
    string VaultTokenId,
    string? CustomerId,
    string? Brand,
    string? LastDigits,
    string? Expiry);

public record GatewayTransaction(
    string? TransactionId,
    string? ReferenceId,
    string? ReferenceIdType,
    string? InvoiceId,
    string? CustomField,
    decimal? Amount,
    string? Currency,
    decimal? Fee,
    string? Status,
    string? EventCode,
    DateTimeOffset? Time);

/// <summary>
/// The payment provider boundary. All PayPal interaction goes through here.
/// </summary>
public interface IPaymentGateway
{
    string Currency { get; }

    Task<AuthorizationResult> AuthorizeWithCardAsync(decimal amount, CardDetails card,
        string idempotencyKey, string invoiceId, CancellationToken ct);

    Task<AuthorizationResult> AuthorizeWithVaultedCardAsync(decimal amount, string vaultTokenId,
        string idempotencyKey, string invoiceId, CancellationToken ct);

    Task<AuthorizationState> GetAuthorizationAsync(string authorizationId, CancellationToken ct);

    Task<AuthorizationState> ReauthorizeAsync(string authorizationId, decimal amount,
        string idempotencyKey, CancellationToken ct);

    Task<CaptureResult> CaptureAsync(string authorizationId, string idempotencyKey,
        string invoiceId, CancellationToken ct);

    Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken ct);

    Task<RefundResult> RefundCaptureAsync(string captureId, decimal? amount,
        string idempotencyKey, CancellationToken ct);

    Task<VaultedCardResult> VaultCardAsync(CardDetails card, string merchantCustomerId,
        string idempotencyKey, CancellationToken ct);

    Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken ct);

    Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken ct);
}
