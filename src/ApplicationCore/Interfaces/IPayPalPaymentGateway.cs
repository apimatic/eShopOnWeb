using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over PayPal for the money movements this app performs. Keeps ApplicationCore free of any
/// SDK dependency; the concrete implementation in Infrastructure talks to PayPal via the paypal-sdk.
/// </summary>
public interface IPayPalPaymentGateway
{
    /// <summary>
    /// Authorize (place a hold for) <paramref name="amount"/> using raw card details, for a one-off payment.
    /// The amount held equals the order total to the cent. <paramref name="requestId"/> is a stable
    /// idempotency key so a retry never places two holds.
    /// </summary>
    Task<PayPalAuthorizationResult> AuthorizeWithCardAsync(decimal amount, string currency, CardDetails card,
        string requestId, CancellationToken cancellationToken = default);

    /// <summary>Authorize using a previously vaulted (saved) card, identified by its vault token.</summary>
    Task<PayPalAuthorizationResult> AuthorizeWithVaultedCardAsync(decimal amount, string currency, string vaultId,
        string requestId, CancellationToken cancellationToken = default);

    /// <summary>Capture (take the money for) an existing authorization at fulfilment.</summary>
    Task<PayPalCaptureResult> CaptureAuthorizationAsync(string authorizationId, string requestId,
        CancellationToken cancellationToken = default);

    /// <summary>Renew a stale authorization so it can still be captured. Throws when it can no longer be renewed.</summary>
    Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        CancellationToken cancellationToken = default);

    /// <summary>Read an authorization's current status from PayPal.</summary>
    Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken = default);

    /// <summary>Void (release) an authorization before capture, so no money moves.</summary>
    Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Refund a captured payment, in full or in part. <paramref name="requestId"/> is the idempotency key.</summary>
    Task<PayPalRefundResult> RefundCaptureAsync(string captureId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken = default);

    /// <summary>Vault (save) a card so it can be charged later without re-entering it. Returns safe display metadata.</summary>
    Task<PayPalVaultResult> VaultCardAsync(CardDetails card, CancellationToken cancellationToken = default);

    /// <summary>
    /// PayPal's own record of transactions for a date range, paged through in full so the whole range is
    /// covered — not just the first page.
    /// </summary>
    Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
