using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The payment-processor abstraction the application talks to. The concrete implementation (PayPal) lives
/// in Infrastructure; ApplicationCore never references the payment SDK. Every method is expected to be
/// idempotent in effect for a given order: a repeated call must not authorize, capture, or refund twice.
/// Implementations translate provider failures into <see cref="Exceptions.PaymentGatewayException"/>.
/// </summary>
public interface IPaymentProcessor
{
    /// <summary>Places a hold on the order total (authorize). Does not take the money.</summary>
    Task<AuthorizationResult> AuthorizeAsync(AuthorizationRequest request, CancellationToken cancellationToken);

    /// <summary>Takes the previously held money (capture) at fulfilment.</summary>
    Task<CaptureResult> CaptureAsync(string paymentReference, string authorizationId, decimal amount,
        string currencyCode, CancellationToken cancellationToken);

    /// <summary>Renews a stale authorization so fulfilment can proceed.</summary>
    Task<ReauthorizeResult> ReauthorizeAsync(string paymentReference, string authorizationId, decimal amount,
        string currencyCode, CancellationToken cancellationToken);

    /// <summary>Releases held funds (void) when an order is cancelled before fulfilment.</summary>
    Task VoidAsync(string paymentReference, string authorizationId, CancellationToken cancellationToken);

    /// <summary>Refunds a captured payment, in full (null amount) or in part, under a caller idempotency key.</summary>
    Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currencyCode,
        string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>Vaults a card so it can be reused later; returns a token and a safe description.</summary>
    Task<SavedCardResult> VaultCardAsync(VaultCardRequest request, CancellationToken cancellationToken);

    /// <summary>Removes a vaulted card so it can no longer fund a payment.</summary>
    Task DeleteVaultedCardAsync(string vaultToken, CancellationToken cancellationToken);

    /// <summary>Lists PayPal's own transaction records over a date range, covering the whole range.</summary>
    Task<IReadOnlyList<ReconciliationTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken);
}
