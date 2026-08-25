using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.PayPalService;

public record CardPaymentDetails(
    string Number,
    string Expiry,
    string SecurityCode,
    string? Name = null);

public record CardVaultRequest(
    string Number,
    string Expiry,
    string SecurityCode,
    string? Name = null);

public record CaptureResult(
    string CaptureId,
    decimal CapturedAmount,
    decimal PayPalFee,
    decimal NetAmount);

public record RefundResult(
    string RefundId);

public record SavedCardInfo(
    string PaymentMethodId,
    string? Last4,
    string? Brand,
    string? Expiry);

public record TransactionRecord(
    string? TransactionId,
    string? PaypalReferenceId,
    string? InvoiceId,
    string? Status,
    string? Amount,
    string? Currency,
    string? FeeAmount,
    string? InitiationDate);

public interface IPayPalService
{
    string Currency { get; }

    Task<string> CreatePayPalOrderAsync(decimal amount, string currency, CancellationToken ct = default);

    Task<string> AuthorizeOrderAsync(
        string paypalOrderId,
        string idempotencyKey,
        CardPaymentDetails? card,
        string? vaultId,
        CancellationToken ct = default);

    Task<(bool IsStale, bool CanReauthorize)> CheckAuthorizationAsync(
        string authorizationId,
        CancellationToken ct = default);

    Task<string> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        CancellationToken ct = default);

    Task<CaptureResult> CaptureAsync(
        string authorizationId,
        string idempotencyKey,
        CancellationToken ct = default);

    Task VoidAsync(string authorizationId, CancellationToken ct = default);

    Task<RefundResult> RefundAsync(
        string captureId,
        decimal? amount,
        string currency,
        string idempotencyKey,
        CancellationToken ct = default);

    Task<string> VaultCardAsync(string customerId, CardVaultRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<SavedCardInfo>> ListCardsAsync(string customerId, CancellationToken ct = default);

    Task DeleteCardAsync(string tokenId, CancellationToken ct = default);

    Task<IReadOnlyList<TransactionRecord>> GetTransactionsAsync(
        string from,
        string to,
        CancellationToken ct = default);
}
