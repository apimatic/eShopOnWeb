using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The application's abstraction over PayPal. All PayPal SDK types stay behind this boundary; the
/// domain and API layers deal only in the plain records under
/// <see cref="Microsoft.eShopWeb.ApplicationCore.Payments"/>. Card details flow in but are never
/// returned, stored by the app, or logged.
/// </summary>
public interface IPayPalPaymentGateway
{
    /// <summary>
    /// Authorize (place a hold for) the order total using a one-off card or a saved (vaulted) card.
    /// The idempotency key makes a retried request safe. Throws
    /// <see cref="Exceptions.PaymentApprovalRequiredException"/> if PayPal would require a browser approval.
    /// </summary>
    Task<AuthorizationResult> AuthorizeAsync(PayPalAuthorizationRequest request, string idempotencyKey, CancellationToken ct);

    /// <summary>Capture a previously authorized hold. Throws <see cref="Exceptions.AuthorizationExpiredException"/>
    /// when the hold is stale and must be renewed first.</summary>
    Task<CaptureResult> CaptureAsync(string authorizationId, string idempotencyKey, CancellationToken ct);

    /// <summary>Renew a stale hold. Throws <see cref="Exceptions.AuthorizationNotRenewableException"/>
    /// when it can no longer be renewed.</summary>
    Task<ReauthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency, CancellationToken ct);

    /// <summary>Void a hold, releasing the funds (cancel before fulfilment).</summary>
    Task VoidAsync(string authorizationId, CancellationToken ct);

    /// <summary>Refund a capture, in full (<paramref name="amount"/> null) or in part. The idempotency
    /// key is caller-supplied; repeating it must not refund twice.</summary>
    Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken ct);

    /// <summary>Vault a card so it can be reused. Returns the vault id and a safe descriptor only.</summary>
    Task<VaultCardResult> VaultCardAsync(PayPalCardData card, CancellationToken ct);

    /// <summary>Delete a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct);

    /// <summary>PayPal's own record of transactions over a date range, covering the whole range (all pages).</summary>
    Task<IReadOnlyList<ReconciliationTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
