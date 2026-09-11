using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The seam between the application and the payment provider (PayPal). Implemented in Infrastructure
/// so that no provider SDK type leaks into ApplicationCore. Every method throws
/// <see cref="PaymentProcessingException"/> on failure.
/// </summary>
public interface IPaymentProcessor
{
    /// <summary>Authorize (hold) the order total. Does not take the money.</summary>
    Task<AuthorizationResult> AuthorizeAsync(AuthorizeRequest request, CancellationToken ct = default);

    /// <summary>Renew a stale/expired authorization hold before fulfilment.</summary>
    Task<AuthorizationResult> ReauthorizeAsync(string payPalOrderId, string authorizationId,
        string currencyCode, decimal amount, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Capture an authorized payment at fulfilment (takes the money).</summary>
    Task<CaptureResult> CaptureAsync(string authorizationId, string currencyCode, decimal amount,
        string idempotencyKey, CancellationToken ct = default);

    /// <summary>Void an authorization before capture, releasing the held funds.</summary>
    Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Refund a captured payment, in full (amount null) or in part.</summary>
    Task<RefundResult> RefundAsync(string captureId, string currencyCode, decimal? amount,
        string idempotencyKey, CancellationToken ct = default);

    /// <summary>Vault (save) a card for future reuse.</summary>
    Task<VaultCardResult> VaultCardAsync(VaultCardRequest request, CancellationToken ct = default);

    /// <summary>Delete a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken ct = default);

    /// <summary>
    /// PayPal's own record of transactions in a date range, across all pages of reporting.
    /// </summary>
    Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct = default);
}
