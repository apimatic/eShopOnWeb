using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;

// Abstracts every PayPal interaction this application needs. ApplicationCore depends only on this
// interface and the plain result records above; the PayPal SDK itself is referenced only by the
// Infrastructure-layer implementation, keeping the domain free of any provider-specific type.
public interface IPaymentGateway
{
    Task<AuthorizationResult> AuthorizeWithCardAsync(decimal amount, string currency, CardDetails card, string idempotencyKey, CancellationToken ct = default);

    Task<AuthorizationResult> AuthorizeWithVaultedCardAsync(decimal amount, string currency, string vaultId, string idempotencyKey, CancellationToken ct = default);

    Task<ReauthorizationResult> ReauthorizeAsync(string authorizationId, CancellationToken ct = default);

    Task<CaptureResult> CaptureAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default);

    Task VoidAsync(string authorizationId, CancellationToken ct = default);

    Task<RefundResult> RefundAsync(string captureId, decimal amount, string currency, string idempotencyKey, CancellationToken ct = default);

    Task<SavedCardResult> SaveCardAsync(CardDetails card, string merchantCustomerId, CancellationToken ct = default);

    Task DeleteSavedCardAsync(string vaultId, CancellationToken ct = default);

    Task<IReadOnlyList<TransactionRecord>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
