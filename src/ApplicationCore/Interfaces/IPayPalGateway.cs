using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Thin abstraction over the PayPal REST API surface this integration uses. The
/// implementation owns OAuth token management, request/idempotency headers, error
/// translation, and (for reconciliation) date-range chunking and pagination.
/// </summary>
public interface IPayPalGateway
{
    // --- Orders / authorize / capture ---

    /// <summary>Create a PayPal order with intent AUTHORIZE for the given amount.</summary>
    Task<PayPalOrderResult> CreateAuthorizationOrderAsync(
        decimal amount, string currencyCode, string invoiceId, string customId, string requestId,
        CancellationToken cancellationToken = default);

    /// <summary>Authorize (hold funds on) an order with raw card details.</summary>
    Task<PayPalAuthorizationResult> AuthorizeOrderWithCardAsync(
        string payPalOrderId, CardPaymentDetails card, string requestId,
        CancellationToken cancellationToken = default);

    /// <summary>Authorize (hold funds on) an order with a previously vaulted card.</summary>
    Task<PayPalAuthorizationResult> AuthorizeOrderWithVaultAsync(
        string payPalOrderId, string vaultId, string requestId,
        CancellationToken cancellationToken = default);

    /// <summary>Fetch the current state of an authorization.</summary>
    Task<PayPalAuthorizationResult> GetAuthorizationAsync(
        string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Renew a hold that is nearing/after its honor period.</summary>
    Task<PayPalAuthorizationResult> ReauthorizeAsync(
        string authorizationId, decimal amount, string currencyCode, string requestId,
        CancellationToken cancellationToken = default);

    /// <summary>Capture (take) an authorized payment in full.</summary>
    Task<PayPalCaptureResult> CaptureAuthorizationAsync(
        string authorizationId, string invoiceId, string requestId,
        CancellationToken cancellationToken = default);

    /// <summary>Void (release) an authorization before capture.</summary>
    Task VoidAuthorizationAsync(
        string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Refund a capture, fully (null amount) or partially.</summary>
    Task<PayPalRefundResult> RefundCaptureAsync(
        string captureId, decimal? amount, string currencyCode, string idempotencyKey,
        CancellationToken cancellationToken = default);

    // --- Vault (saved cards) ---

    /// <summary>Vault a card (setup token → payment token) and return its safe descriptor.</summary>
    Task<VaultedCard> VaultCardAsync(
        CardPaymentDetails card, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Delete a vaulted card so it can no longer be charged.</summary>
    Task DeleteVaultedCardAsync(
        string vaultId, CancellationToken cancellationToken = default);

    // --- Reconciliation ---

    /// <summary>
    /// List PayPal's own record of transactions across the whole [from, to] range,
    /// transparently chunking the range to PayPal's maximum window and paging each chunk.
    /// </summary>
    Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
