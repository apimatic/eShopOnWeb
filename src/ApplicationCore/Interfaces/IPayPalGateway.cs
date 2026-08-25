using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPayPalGateway
{
    Task<AuthorizeResult> AuthorizeAsync(int orderId, decimal amount, string currency, CardDetails card, CancellationToken ct = default);
    Task<AuthorizeResult> AuthorizeWithVaultAsync(int orderId, decimal amount, string currency, string vaultId, CancellationToken ct = default);
    Task<CaptureResult> CaptureAsync(int orderId, string authorizationId, CancellationToken ct = default);
    Task VoidAsync(string authorizationId, CancellationToken ct = default);
    Task<ReauthorizeResult> ReauthorizeAsync(int orderId, string authorizationId, decimal amount, string currency, CancellationToken ct = default);
    Task<RefundResult> RefundAsync(string captureId, decimal? amount, string? currency, string idempotencyKey, CancellationToken ct = default);
    Task<System.Collections.Generic.IReadOnlyList<TransactionRecord>> GetTransactionsAsync(string startDate, string endDate, CancellationToken ct = default);
    Task<VaultResult> VaultCardAsync(string merchantCustomerId, CardDetails card, CancellationToken ct = default);
    Task<System.Collections.Generic.IReadOnlyList<VaultedCard>> ListVaultedCardsAsync(string payPalCustomerId, CancellationToken ct = default);
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct = default);
}

public record CardDetails(string? Name, string Number, string Expiry, string SecurityCode);
public record AuthorizeResult(string PayPalOrderId, string AuthorizationId, System.DateTimeOffset ExpiresAt);
public record CaptureResult(string CaptureId, decimal CapturedAmount, decimal? PayPalFee, decimal? NetAmount);
public record ReauthorizeResult(string NewAuthorizationId, System.DateTimeOffset NewExpiresAt);
public record RefundResult(string RefundId, decimal Amount);
public record TransactionRecord(string TransactionId, string? ReferenceId, string? Status, decimal? Amount, decimal? Fee, string? InvoiceId, string? InitiationDate);
public record VaultResult(string VaultId, string? PayPalCustomerId, string? Last4, string? Brand, string? Expiry);
public record VaultedCard(string VaultId, string? Last4, string? Brand, string? Expiry);
