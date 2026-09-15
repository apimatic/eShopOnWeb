using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

/// <summary>
/// The single seam through which the application talks to PayPal. All money movement (holds,
/// captures, refunds), card vaulting and transaction reporting go through here. The concrete
/// implementation lives in the Infrastructure layer and owns HTTP, OAuth and configuration.
/// </summary>
public interface IPayPalGateway
{
    /// <summary>The configured ISO-4217 currency code used for every PayPal amount.</summary>
    string Currency { get; }

    /// <summary>Creates a PayPal order and authorizes it — a hold on the money, not a capture.</summary>
    Task<PayPalAuthorizationResult> AuthorizeAsync(PayPalAuthorizationRequest request, CancellationToken cancellationToken = default);

    /// <summary>Captures (takes) a previously authorized payment, fully.</summary>
    Task<PayPalCaptureResult> CaptureAsync(string authorizationId, decimal amount, string invoiceId, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Refreshes a hold that has gone (or is about to go) stale before fulfilment.</summary>
    Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Releases the hold on an authorization without charging (cancel before fulfilment).</summary>
    Task VoidAsync(string authorizationId, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Refunds a captured payment, in full (amount null) or in part.</summary>
    Task<PayPalRefundResult> RefundAsync(string captureId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Vaults a card so it can be reused later, returning a durable payment token.</summary>
    Task<PayPalVaultedCardResult> VaultCardAsync(PayPalCardDetails card, string? existingCustomerId, string merchantCustomerId, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Removes a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists PayPal's own record of transactions across the whole date range (chunked into the
    /// windows PayPal allows and paged to completion), for reconciliation against eShop orders.
    /// </summary>
    Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
