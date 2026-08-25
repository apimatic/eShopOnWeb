using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.PayPal;

public record CardDetails(
    string Number,
    string Expiry,
    string Cvv,
    string CardholderName,
    string BillingCountry,
    string? BillingStreet = null,
    string? BillingCity = null,
    string? BillingState = null,
    string? BillingZip = null);

public interface IPayPalService
{
    Task<AuthorizeResult> AuthorizeWithCardAsync(decimal amount, string currency, CardDetails card, string orderRef, CancellationToken ct = default);
    Task<AuthorizeResult> AuthorizeWithVaultTokenAsync(decimal amount, string currency, string vaultToken, string orderRef, CancellationToken ct = default);
    Task<CaptureResult> CaptureAsync(string authorizationId, CancellationToken ct = default);
    Task VoidAsync(string authorizationId, CancellationToken ct = default);
    Task<RefundResult> RefundAsync(string captureId, string idempotencyKey, decimal? amount, string currency, CancellationToken ct = default);
    Task<string> ReauthorizeAsync(string authorizationId, decimal amount, string currency, CancellationToken ct = default);
    Task<VaultCardResult> VaultCardAsync(CardDetails card, string? existingPayPalCustomerId, string merchantCustomerId, CancellationToken ct = default);
    Task DeleteVaultTokenAsync(string vaultToken, CancellationToken ct = default);
    Task<IReadOnlyList<TransactionItem>> SearchTransactionsAsync(string startDate, string endDate, CancellationToken ct = default);
}
