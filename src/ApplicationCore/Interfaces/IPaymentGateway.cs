using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the payment processor (PayPal). Keeps the application core free of any SDK type — the
/// Infrastructure implementation is the only place the PayPal Server SDK is referenced. Every method
/// throws <see cref="Exceptions.PaymentGatewayException"/> on a processor failure so callers have a single
/// failure type to reason about.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>The configured settlement currency (ISO-4217), from <c>PayPal:Currency</c>.</summary>
    string Currency { get; }

    /// <summary>Place a hold (authorize) for <paramref name="request"/> without taking the money.</summary>
    Task<AuthorizationResult> AuthorizeAsync(AuthorizeCardRequest request, CancellationToken ct = default);

    /// <summary>Renew a stale hold. Returns a new authorization id.</summary>
    Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string idempotencyKey,
        CancellationToken ct = default);

    /// <summary>Take the money for an authorized payment (capture at fulfilment).</summary>
    Task<CaptureResult> CaptureAsync(string authorizationId, string idempotencyKey,
        CancellationToken ct = default);

    /// <summary>Release a hold before fulfilment so no money moves.</summary>
    Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Refund a captured payment, in full (<paramref name="amount"/> null) or in part.</summary>
    Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currency, string idempotencyKey,
        string orderReference, CancellationToken ct = default);

    /// <summary>Vault (save) a card for later reuse. Returns a safe descriptor plus the vault token id.</summary>
    Task<SavedCardResult> SaveCardAsync(SaveCardRequest request, string? existingCustomerId,
        CancellationToken ct = default);

    /// <summary>List the vaulted cards for a PayPal customer.</summary>
    Task<IReadOnlyList<SavedCardResult>> ListCardsAsync(string customerId, CancellationToken ct = default);

    /// <summary>Delete a vaulted card by its token id.</summary>
    Task DeleteCardAsync(string paymentMethodId, CancellationToken ct = default);

    /// <summary>
    /// PayPal's own record of transactions across a date range, covering the whole range (all pages), for
    /// reconciliation against eShop orders.
    /// </summary>
    Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct = default);
}
