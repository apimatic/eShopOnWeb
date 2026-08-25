using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPayPalService
{
    Task<PaymentAuthorizationResult> AuthorizeWithCardAsync(decimal amount, string currency, string idempotencyKey, CardPaymentDetails card, CancellationToken ct = default);
    Task<PaymentAuthorizationResult> AuthorizeWithVaultTokenAsync(decimal amount, string currency, string idempotencyKey, string vaultTokenId, CancellationToken ct = default);
    Task<CapturePaymentResult> CapturePaymentAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default);
    Task VoidPaymentAsync(string authorizationId, CancellationToken ct = default);
    Task<RefundPaymentResult> RefundPaymentAsync(string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken ct = default);
    Task<IReadOnlyList<TransactionRecord>> GetTransactionsAsync(string startDate, string endDate, CancellationToken ct = default);
    Task<VaultTokenResult> CreateVaultTokenAsync(string customerId, CardVaultDetails card, string idempotencyKey, CancellationToken ct = default);
    Task<IReadOnlyList<VaultTokenInfo>> ListVaultTokensAsync(string customerId, CancellationToken ct = default);
    Task DeleteVaultTokenAsync(string tokenId, CancellationToken ct = default);
}

public record CardPaymentDetails(string Number, string Expiry, string SecurityCode, string? Name);
public record CardVaultDetails(string Number, string Expiry, string SecurityCode, string? Name);
public record PaymentAuthorizationResult(string PayPalOrderId, string AuthorizationId, string? ExpirationTime);
public record CapturePaymentResult(string CaptureId, decimal CapturedAmount, string Currency, decimal Fee, decimal NetAmount);
public record RefundPaymentResult(string RefundId, decimal Amount, string Currency, decimal TotalRefunded);
public record TransactionRecord(string? TransactionId, decimal? Amount, string? Currency, decimal? Fee, string? Status, string? InitiationDate);
public record VaultTokenResult(string TokenId, string? CustomerId, string? Last4, string? Brand, string? Expiry);
public record VaultTokenInfo(string TokenId, string? Last4, string? Brand, string? Expiry);
