using System;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Integrations.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the PayPal REST API used for this integration:
/// authorizations (holds), captures, voids, refunds, card vaulting and transaction reporting.
/// </summary>
public interface IPayPalProvider
{
    /// <summary>
    /// Creates a PayPal order with intent=AUTHORIZE for the given amount and authorizes it,
    /// placing a hold for exactly that amount. Funding source is either one-off card details
    /// or a previously vaulted card (vaultId).
    /// </summary>
    /// <param name="requestId">PayPal-Request-Id value making this call idempotent.</param>
    Task<PayPalAuthorizationResult> AuthorizeAsync(
        decimal amount,
        string currency,
        CardDetails? card,
        string? vaultId,
        string? invoiceId,
        string? customId,
        string requestId,
        bool storeCardInVault = false);

    /// <summary>Reads the current status of an authorization directly from PayPal.</summary>
    Task<PayPalAuthorizationStatus?> GetAuthorizationStatusAsync(string authorizationId);

    /// <summary>Captures (takes) the money held by an authorization.</summary>
    /// <param name="requestId">PayPal-Request-Id value making this call idempotent.</param>
    Task<PayPalCaptureResult> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currency, string requestId, bool finalCapture = true);

    /// <summary>
    /// Reauthorizes an expired authorization via PayPal's reauthorize endpoint.
    /// Only succeeds while PayPal still allows it (before the original authorization is
    /// too old); failures are reported as <see cref="Exceptions.PaymentDeclinedException"/>.
    /// </summary>
    /// <param name="requestId">PayPal-Request-Id value making this call idempotent.</param>
    Task<PayPalAuthorizationResult> ReauthorizeAsync(string staleAuthorizationId, decimal amount, string currency, string requestId);

    /// <summary>Voids (releases) an authorization so no money moves.</summary>
    /// <param name="requestId">PayPal-Request-Id value making this call idempotent.</param>
    Task<PayPalVoidResult> VoidAuthorizationAsync(string authorizationId, string requestId);

    /// <summary>
    /// Refunds part or all of a captured payment.
    /// </summary>
    /// <param name="requestId">
    /// PayPal-Request-Id (caller supplied idempotency key): repeating the request under the
    /// same key returns PayPal's original refund instead of refunding again.
    /// </param>
    Task<PayPalRefundResult> RefundCaptureAsync(string captureId, decimal amount, string currency, string requestId, string? noteToPayer = null);

    /// <summary>Vaults (saves) a card for a customer, returning its vault id + non-sensitive description.</summary>
    Task<PayPalVaultResult> VaultCardAsync(CardDetails card, string customerId, string requestId);

    /// <summary>Deletes a previously vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultCardAsync(string vaultId);

    /// <summary>Fetches one page of PayPal's own transaction report for a date range.</summary>
    Task<PayPalTransactionPage> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to, int page, int pageSize);
}
