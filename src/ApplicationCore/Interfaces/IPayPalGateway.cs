using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The application's sole gateway to PayPal. Everything the payment flows need from PayPal goes
/// through here; the implementation owns HTTP, auth-token caching, idempotency headers and mapping.
/// </summary>
public interface IPayPalGateway
{
    /// <summary>
    /// Places a hold on the buyer's funds for <paramref name="amount"/> and returns the authorization.
    /// Funds are held, not taken. Uses a single-step Orders create with the card/vault payment source.
    /// </summary>
    Task<PayPalAuthorizationResult> AuthorizeAsync(
        decimal amount, string currency, string invoiceId, string customId,
        AuthorizeInstruction instruction, string requestId, CancellationToken ct = default);

    /// <summary>Captures (settles) an authorized payment. Money actually moves here.</summary>
    Task<PayPalCaptureResult> CaptureAsync(
        string authorizationId, decimal amount, string currency, string invoiceId,
        bool finalCapture, string requestId, CancellationToken ct = default);

    /// <summary>Refreshes a hold that is nearing/past expiry so it can still be captured.</summary>
    Task<PayPalAuthorizationResult> ReauthorizeAsync(
        string authorizationId, decimal amount, string currency, string requestId, CancellationToken ct = default);

    /// <summary>Releases a hold without charging (cancel before fulfilment).</summary>
    Task VoidAsync(string authorizationId, string requestId, CancellationToken ct = default);

    /// <summary>Refunds a captured payment, fully (null amount) or partially.</summary>
    Task<PayPalRefundResult> RefundAsync(
        string captureId, decimal? amount, string currency, string invoiceId,
        string requestId, CancellationToken ct = default);

    /// <summary>Vaults a card for reuse and returns only safe-to-display metadata plus the vault id.</summary>
    Task<PayPalVaultCardResult> VaultCardAsync(
        CardDetails card, string? customerId, string requestId, CancellationToken ct = default);

    /// <summary>Removes a vaulted card so it can no longer be charged.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct = default);

    /// <summary>
    /// Returns PayPal's own record of transactions across the whole <paramref name="from"/>..<paramref name="to"/>
    /// range (chunked and paged internally), for reconciliation against eShop orders.
    /// </summary>
    Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
