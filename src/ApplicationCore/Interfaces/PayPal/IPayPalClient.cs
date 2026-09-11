using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

/// <summary>
/// Thin, verified wrapper over the PayPal REST API. Every amount is expressed as a decimal in
/// the merchant currency; the implementation formats it to the currency's minor units and never
/// hard-codes currency values. All operations that mutate money accept an idempotency request id.
/// </summary>
public interface IPayPalClient
{
    /// <summary>
    /// Creates a PayPal order with intent AUTHORIZE using raw card details, placing a hold on the
    /// order total. The card is passed straight to PayPal and never stored locally.
    /// </summary>
    Task<PayPalAuthorizationResult> AuthorizeWithCardAsync(
        decimal amount, string invoiceId, string customId, CardDetails card, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Authorizes the order total against a previously-vaulted card (by vault id).</summary>
    Task<PayPalAuthorizationResult> AuthorizeWithVaultedCardAsync(
        decimal amount, string invoiceId, string customId, string vaultId, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Captures (takes) an authorization at fulfilment.</summary>
    Task<PayPalCaptureResult> CaptureAuthorizationAsync(
        string authorizationId, decimal amount, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Returns the current status of an authorization (e.g. CREATED, CAPTURED, EXPIRED, VOIDED).</summary>
    Task<string> GetAuthorizationStatusAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Renews a stale authorization, yielding a new authorization id to capture against.</summary>
    Task<PayPalReauthorizeResult> ReauthorizeAsync(
        string authorizationId, decimal amount, CancellationToken cancellationToken = default);

    /// <summary>Voids (releases) an authorization before capture, so no money moves.</summary>
    Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Refunds a captured payment in full (null amount) or in part.</summary>
    Task<PayPalRefundResult> RefundCaptureAsync(
        string captureId, decimal? amount, string invoiceId, string customId, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Vaults a card for reuse, linking it to an existing PayPal customer id when supplied.</summary>
    Task<PayPalVaultCardResult> VaultCardAsync(
        CardDetails card, string? existingCustomerId, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Removes a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists PayPal's own record of transactions across the whole date range (chunking to respect
    /// PayPal's 31-day window limit and paging through every page).
    /// </summary>
    Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
