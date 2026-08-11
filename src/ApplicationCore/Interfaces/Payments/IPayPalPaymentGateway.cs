using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

/// <summary>
/// Abstraction over PayPal. The application talks to PayPal only through this interface, so the
/// HTTP/JSON details stay in Infrastructure and the domain sees typed results.
/// </summary>
public interface IPayPalPaymentGateway
{
    /// <summary>The ISO-4217 currency configured for this merchant (from the <c>PayPal:Currency</c> setting).</summary>
    string Currency { get; }

    /// <summary>
    /// Creates an order with PayPal for the given amount and authorizes it (places a hold) using the
    /// supplied instrument. <paramref name="orderReference"/> (the eShop order id) is stamped onto the
    /// transaction so it can be reconciled later. Does not capture.
    /// </summary>
    Task<AuthorizationResult> AuthorizeOrderAsync(string orderReference, decimal amount, string currency,
        PaymentInstrument instrument, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Fetches the current state of an authorization (status, expiry).</summary>
    Task<AuthorizationResult> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Renews a hold that is nearing/at expiry, returning the (possibly new) authorization.</summary>
    Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        CancellationToken cancellationToken = default);

    /// <summary>Captures (settles) an authorization, in full. Returns the captured amount, fee and net.</summary>
    Task<CaptureResult> CaptureAuthorizationAsync(string authorizationId, string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>Releases a hold without charging.</summary>
    Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Refunds a capture, fully (null amount) or partially. Idempotent on the supplied key.</summary>
    Task<RefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currency,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Vaults (saves) a card for reuse and returns safe metadata plus the vault token id.</summary>
    Task<VaultResult> VaultCardAsync(CardDetails card, string customerReference,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists PayPal's own record of transactions across the whole date range (chunked and fully
    /// paginated), for reconciliation against eShop orders.
    /// </summary>
    Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
