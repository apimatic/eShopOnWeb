using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record CardPaymentDetails(
    string CardNumber,
    int ExpiryMonth,
    int ExpiryYear,
    string Cvv,
    string CardholderName,
    string CountryCode,
    string? Street = null,
    string? City = null,
    string? State = null,
    string? PostalCode = null
);

public record AuthorizationResult(
    string AuthorizationId,
    string Status,
    string? ExpiresAt
);

public record CaptureResult(
    string CaptureId,
    string Status,
    string? Amount,
    string? PayPalFee,
    string? NetAmount
);

public record RefundResult(
    string RefundId,
    string Status,
    string? Amount
);

public record VaultTokenResult(
    string TokenId,
    string? Last4,
    string? Brand,
    string? Expiry,
    string? CardType
);

public record TransactionSearchResult(
    string TransactionId,
    string? Status,
    string? Amount,
    string? Currency,
    string? InitiationDate,
    string? PayPalReferenceId,
    string? ReferenceType
);

public interface IPayPalPaymentService
{
    Task<string> CreateOrderAsync(decimal total, string currency, string idempotencyKey, CancellationToken ct = default);
    Task<AuthorizationResult> AuthorizeWithCardAsync(string paypalOrderId, CardPaymentDetails card, string idempotencyKey, CancellationToken ct = default);
    Task<AuthorizationResult> AuthorizeWithVaultTokenAsync(string paypalOrderId, string vaultTokenId, string idempotencyKey, CancellationToken ct = default);
    Task<CaptureResult> CaptureAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default);
    Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default);
    Task VoidAuthorizationAsync(string authorizationId, CancellationToken ct = default);
    Task<RefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken ct = default);
    Task<VaultTokenResult> VaultCardAsync(string customerId, CardPaymentDetails card, CancellationToken ct = default);
    Task<IReadOnlyList<VaultTokenResult>> ListVaultedTokensAsync(string customerId, CancellationToken ct = default);
    Task DeleteVaultedTokenAsync(string tokenId, CancellationToken ct = default);
    Task<IReadOnlyList<TransactionSearchResult>> SearchTransactionsAsync(string startDate, string endDate, CancellationToken ct = default);
}
