using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

/// <summary>
/// The application's single gateway to PayPal. Every PayPal interaction goes through this
/// abstraction; the implementation talks the PayPal REST API and is the only place that knows
/// about HTTP, tokens, and JSON. Callers work in domain terms (amounts, ids, statuses).
/// </summary>
public interface IPayPalClient
{
    /// <summary>Creates a checkout order with intent=AUTHORIZE for the given amount. No funds move yet.</summary>
    Task<CreateOrderResult> CreateAuthorizationOrderAsync(
        decimal amount, string currency, string merchantReference, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Authorizes (holds funds for) a created order using the supplied card or vaulted card.</summary>
    Task<AuthorizationResult> AuthorizeOrderAsync(
        string payPalOrderId, PaymentInstrument instrument, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Reads the current state of an authorization, including its expiry.</summary>
    Task<AuthorizationResult> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Renews a hold that is nearing/​past expiry, refreshing the honor period.</summary>
    Task<AuthorizationResult> ReauthorizeAsync(
        string authorizationId, decimal amount, string currency, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Captures (settles) an authorization, taking the money. Returns the fee breakdown.</summary>
    Task<CaptureResult> CaptureAuthorizationAsync(
        string authorizationId, decimal amount, string currency, string requestId, bool finalCapture, CancellationToken cancellationToken = default);

    /// <summary>Voids an authorization, releasing the hold so no money moves.</summary>
    Task VoidAuthorizationAsync(string authorizationId, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Refunds a capture, in full (null amount) or in part.</summary>
    Task<RefundResult> RefundCaptureAsync(
        string captureId, decimal? amount, string currency, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Vaults a card for later reuse, returning the token id and safe descriptors.</summary>
    Task<VaultCardResult> VaultCardAsync(
        CardDetails card, string? customerId, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Deletes a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultTokenAsync(string vaultId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists PayPal's own record of transactions over a date range, paging and date-chunking as
    /// needed so the whole range is covered — not just the first page.
    /// </summary>
    Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
