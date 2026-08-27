using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the payment provider (PayPal). Implementations must never
/// persist or log full card details.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>Creates a provider order with intent AUTHORIZE for the exact amount.</summary>
    Task<PayPalOrderResult> CreateOrderAsync(decimal amount, string currency,
        string referenceId, string invoiceId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Authorizes (holds) the order total, either with one-off card details or with a
    /// vaulted card (vaultTokenId). Exactly one of the two must be supplied.
    /// </summary>
    Task<PayPalAuthorizationResult> AuthorizeOrderAsync(string payPalOrderId,
        CardDetails? card, string? vaultTokenId, string idempotencyKey, CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Renews a stale authorization hold.</summary>
    Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Captures an authorization (takes the money). Returns PayPal's fee breakdown.</summary>
    Task<PayPalCaptureResult> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currency,
        string invoiceId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Voids an authorization, releasing the shopper's held funds.</summary>
    Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Refunds a capture in full (amount null) or in part.</summary>
    Task<PayPalRefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currency,
        string idempotencyKey, string? noteToPayer, CancellationToken cancellationToken = default);

    /// <summary>Vaults a card and returns the token plus safe display data.</summary>
    Task<PayPalVaultTokenResult> VaultCardAsync(CardDetails card, string customerId,
        string idempotencyKey, CancellationToken cancellationToken = default);

    Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken cancellationToken = default);

    /// <summary>
    /// PayPal's own record of transactions in [from, to]. Covers the whole range:
    /// pages exhaustively and chunks ranges wider than the provider's 31-day window.
    /// </summary>
    Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
