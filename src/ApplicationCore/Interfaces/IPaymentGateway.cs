using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the PayPal payment processor. The single implementation
/// (<c>Infrastructure.PayPal.PayPalClient</c>) is built by hand against the PayPal OpenAPI
/// specifications in <c>api-specs/paypal</c>, which are the authoritative contract for every
/// interaction. Every method is idempotent in effect when given a stable
/// <c>idempotencyKey</c> (sent to PayPal as the <c>PayPal-Request-Id</c> header).
/// </summary>
public interface IPaymentGateway
{
    /// <summary>Authorize (hold) the amount against raw card details for a one-off payment.</summary>
    Task<AuthorizeResult> AuthorizeWithCardAsync(Money amount, CardDetails card, string invoiceId,
        string customId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Authorize (hold) the amount against a previously vaulted card token.</summary>
    Task<AuthorizeResult> AuthorizeWithVaultTokenAsync(Money amount, string vaultTokenId, string invoiceId,
        string customId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Capture (take) a previously authorized payment for the given amount.</summary>
    Task<CaptureResult> CaptureAsync(string authorizationId, Money amount, string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>Renew a stale authorization so it can be captured.</summary>
    Task<ReauthorizeResult> ReauthorizeAsync(string authorizationId, Money amount, string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>Void an authorization, releasing the shopper's held funds.</summary>
    Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Refund a captured payment, in full (amount null) or in part.</summary>
    Task<RefundResult> RefundAsync(string captureId, Money? amount, string invoiceId, string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>Vault a card so it can be reused for later payments.</summary>
    Task<VaultCardResult> VaultCardAsync(CardDetails card, string paypalCustomerId, string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>Remove a vaulted card token so it can no longer be used to pay.</summary>
    Task DeleteVaultTokenAsync(string vaultTokenId, CancellationToken cancellationToken = default);

    /// <summary>
    /// List PayPal's own record of transactions for a date range, following pagination so the whole
    /// range is covered rather than only the first page.
    /// </summary>
    Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
