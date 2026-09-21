using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The application's boundary to PayPal. Implemented in Infrastructure over the PayPal Server SDK; the
/// application layer depends only on this interface and the plain DTOs in
/// <see cref="Microsoft.eShopWeb.ApplicationCore.Payments"/>, never on SDK types.
///
/// Every method translates PayPal/transport failures into a
/// <see cref="Microsoft.eShopWeb.ApplicationCore.Exceptions.PaymentProcessingException"/> carrying a
/// caller-safe message and an HTTP status.
/// </summary>
public interface IPayPalPaymentGateway
{
    /// <summary>
    /// Authorizes (places a hold for) <paramref name="amount"/> in <paramref name="currency"/> using the
    /// given <paramref name="instrument"/>. Does not take the money. <paramref name="invoiceReference"/> is
    /// stamped onto the PayPal order (invoice_id/custom_id) for later reconciliation and must be unique per
    /// order; <paramref name="idempotencyKey"/> makes a repeated call a no-op at PayPal.
    /// </summary>
    Task<AuthorizationOutcome> AuthorizeAsync(decimal amount, string currency, string invoiceReference,
        PaymentInstrument instrument, string idempotencyKey, CancellationToken ct);

    /// <summary>Reads the current state of an authorization (status and expiry) from PayPal.</summary>
    Task<AuthorizationOutcome> GetAuthorizationAsync(string authorizationId, CancellationToken ct);

    /// <summary>Renews a stale authorization for the same amount, returning the renewed hold.</summary>
    Task<AuthorizationOutcome> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        CancellationToken ct);

    /// <summary>Captures (takes the money for) an authorized payment at fulfilment.</summary>
    Task<CaptureOutcome> CaptureAsync(string authorizationId, decimal amount, string currency,
        string idempotencyKey, CancellationToken ct);

    /// <summary>Voids an authorization, releasing the held funds before capture.</summary>
    Task VoidAsync(string authorizationId, CancellationToken ct);

    /// <summary>
    /// Refunds a captured payment. <paramref name="amount"/> null means a full refund; otherwise a partial
    /// refund of that amount. <paramref name="idempotencyKey"/> is the caller-supplied key.
    /// </summary>
    Task<RefundOutcome> RefundAsync(string captureId, decimal? amount, string currency,
        string idempotencyKey, CancellationToken ct);

    /// <summary>Vaults a card for later reuse and returns safe display details plus its vault-token id.</summary>
    Task<VaultCardOutcome> VaultCardAsync(CardDetails card, string idempotencyKey, CancellationToken ct);

    /// <summary>Deletes a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct);

    /// <summary>
    /// Lists PayPal's own record of transactions across a date range (covering every page), for
    /// reconciliation against eShop orders.
    /// </summary>
    Task<IReadOnlyList<ReconciliationTransaction>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken ct);
}
