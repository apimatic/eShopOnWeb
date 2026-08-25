using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over PayPal for the operations this app needs: authorize/capture/void/refund an
/// order payment, vault and reuse saved cards, and search PayPal's own transaction history for
/// reconciliation. Implemented in Infrastructure against the PayPal SDK so ApplicationCore stays
/// free of any PayPal-specific type.
/// </summary>
public interface IPayPalGateway
{
    /// <summary>
    /// Authorizes (holds, does not capture) <paramref name="amount"/> against either a raw card
    /// or a vaulted card. <paramref name="idempotencyKey"/> is sent as PayPal's own request-id
    /// header so a retried call never authorizes twice.
    /// </summary>
    Task<AuthorizePaymentOutcome> AuthorizeAsync(decimal amount, string currency, PaymentSourceRequest paymentSource, string idempotencyKey, CancellationToken ct);

    Task<AuthorizationSnapshot> GetAuthorizationAsync(string authorizationId, CancellationToken ct);

    /// <summary>Renews a stale authorization for the same amount. Throws <see cref="Exceptions.ReauthorizationNotPossibleException"/> when PayPal reports it can no longer be renewed.</summary>
    Task<AuthorizationSnapshot> ReauthorizeAsync(string authorizationId, CancellationToken ct);

    Task<CaptureResult> CaptureAsync(string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken ct);

    Task VoidAsync(string authorizationId, CancellationToken ct);

    /// <summary>Refunds a capture in full (amount == null) or in part. <paramref name="idempotencyKey"/> makes a retried refund a no-op instead of a second refund.</summary>
    Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken ct);

    /// <summary>Vaults a raw card. <paramref name="payPalCustomerId"/> is null the first time a buyer saves a card; the returned <see cref="SavedCard.PayPalCustomerId"/> must be persisted and passed on every later call for the same buyer.</summary>
    Task<SavedCard> SaveCardAsync(string? payPalCustomerId, string merchantBuyerId, CardDetails card, CancellationToken ct);

    Task<IReadOnlyList<SavedCard>> ListSavedCardsAsync(string payPalCustomerId, CancellationToken ct);

    Task DeleteSavedCardAsync(string vaultId, CancellationToken ct);

    /// <summary>Lists every PayPal transaction in [from, to), walking all result pages.</summary>
    Task<IReadOnlyList<PayPalTransactionRecord>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
