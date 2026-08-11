using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The payment processor (PayPal) as the application sees it. Every method maps onto a single
/// PayPal REST capability; all amounts are in the gateway's configured <see cref="Currency"/>.
/// Implementations are responsible for authentication, idempotency headers and translating
/// PayPal error payloads into <see cref="Exceptions.PaymentGatewayException"/>.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>The ISO-4217 currency the merchant transacts in (from configuration).</summary>
    string Currency { get; }

    /// <summary>
    /// Places a hold for <paramref name="amount"/> using raw card details (a one-off payment).
    /// The card is sent straight to PayPal and never stored by this application.
    /// </summary>
    Task<PayPalAuthorizationResult> AuthorizeWithCardAsync(
        decimal amount, string idempotencyKey, CardDetails card, string invoiceId, CancellationToken cancellationToken);

    /// <summary>Places a hold for <paramref name="amount"/> using a previously vaulted card.</summary>
    Task<PayPalAuthorizationResult> AuthorizeWithVaultAsync(
        decimal amount, string idempotencyKey, string vaultId, string invoiceId, CancellationToken cancellationToken);

    /// <summary>Reads the current state of an authorization (status and expiry).</summary>
    Task<PayPalAuthorizationDetails> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken);

    /// <summary>Renews a stale hold, returning the freshly created authorization.</summary>
    Task<PayPalAuthorizationResult> ReauthorizeAsync(
        string authorizationId, decimal amount, string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>Captures (takes) the held funds, returning the fee and net proceeds PayPal reported.</summary>
    Task<PayPalCaptureResult> CaptureAsync(
        string authorizationId, decimal amount, string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>Releases a hold before capture, so no money moves.</summary>
    Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken);

    /// <summary>
    /// Refunds a capture, in full when <paramref name="amount"/> is null, otherwise partially.
    /// </summary>
    Task<PayPalRefundResult> RefundAsync(
        string captureId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>
    /// Vaults a card so it can be reused. Pass an existing PayPal customer id to add the card to
    /// that customer, or null to let PayPal create one. Returns the token and a safe card summary.
    /// </summary>
    Task<VaultedCard> VaultCardAsync(CardDetails card, string? existingCustomerId, CancellationToken cancellationToken);

    /// <summary>Deletes a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns PayPal's own record of transactions across the whole [from, to] range, transparently
    /// walking PayPal's 31-day window and page limits so nothing beyond the first page is missed.
    /// </summary>
    Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}
