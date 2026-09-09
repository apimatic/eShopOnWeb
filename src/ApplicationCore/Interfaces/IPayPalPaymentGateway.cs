using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The application's single gateway to PayPal. Every PayPal interaction goes through this abstraction;
/// the implementation owns OAuth, the base URL, idempotency headers and error translation.
/// </summary>
public interface IPayPalPaymentGateway
{
    /// <summary>The settlement currency, from configuration (<c>PayPal:Currency</c>).</summary>
    string Currency { get; }

    /// <summary>
    /// Places a hold on the shopper's funds equal to the order total, using either raw card details
    /// or a saved card's vault id. Does not take the money. Throws
    /// <see cref="PayPalChallengeRequiredException"/> if PayPal requires a browser approval step.
    /// </summary>
    Task<PayPalAuthorizationResult> AuthorizeAsync(PayPalAuthorizationRequest request, CancellationToken cancellationToken = default);

    /// <summary>Captures (settles) an authorization, returning the fee and net proceeds PayPal reports.</summary>
    Task<PayPalCaptureResult> CaptureAsync(string authorizationId, decimal amount, bool finalCapture, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Renews a stale authorization, minting a fresh hold and honor period.</summary>
    Task<PayPalReauthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, CancellationToken cancellationToken = default);

    /// <summary>Releases an authorization hold without taking any money.</summary>
    Task VoidAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Refunds a capture, fully (null amount) or partially, keyed for idempotency.</summary>
    Task<PayPalRefundResult> RefundAsync(string captureId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Saves (vaults) a card in PayPal's PCI-compliant vault, returning a token and safe descriptor.</summary>
    Task<VaultedCard> VaultCardAsync(CardDetails card, string? payPalCustomerId, CancellationToken cancellationToken = default);

    /// <summary>Removes a saved card from PayPal's vault so it can no longer be charged.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns PayPal's own record of transactions over a date range, covering the whole range
    /// (chunking and paging as required by the reporting API), for reconciliation against eShop orders.
    /// </summary>
    Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
