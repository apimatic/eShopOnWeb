using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

/// <summary>
/// The single boundary through which the application talks to PayPal. All PayPal interactions go through
/// this abstraction; the concrete implementation lives in Infrastructure and is the only place that knows
/// the PayPal REST wire format.
/// </summary>
public interface IPayPalPaymentGateway
{
    /// <summary>The configured settlement currency (ISO code) for this merchant.</summary>
    string Currency { get; }

    /// <summary>Creates a PayPal order with intent=AUTHORIZE for the given amount and returns its id.</summary>
    Task<string> CreateOrderForAuthorizationAsync(
        decimal amount, string currency, string invoiceId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Authorizes (holds) the funds for a PayPal order using a one-off or vaulted card.</summary>
    Task<PayPalAuthorizationResult> AuthorizeOrderAsync(
        string payPalOrderId, PayPalPaymentSource source, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Captures (settles) an authorization. Marks it as the final capture.</summary>
    Task<PayPalCaptureResult> CaptureAuthorizationAsync(
        string authorizationId, decimal amount, string currency, string invoiceId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Renews a stale authorization so it can be captured. Returns the (possibly new) authorization.</summary>
    Task<PayPalReauthorizeResult> ReauthorizeAsync(
        string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Voids an authorization, releasing the held funds without charging.</summary>
    Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Refunds a capture, fully (null amount) or partially.</summary>
    Task<PayPalRefundResult> RefundCaptureAsync(
        string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Vaults a card for future reuse and returns the durable token plus a safe description.
    /// When <paramref name="existingCustomerId"/> is a PayPal-generated customer id it is reused so the
    /// shopper's cards group under one customer; otherwise <paramref name="merchantCustomerId"/> is sent
    /// and PayPal generates the customer id (returned in the result).
    /// </summary>
    Task<PayPalVaultCardResult> VaultCardAsync(
        PayPalCardDetails card, string? existingCustomerId, string merchantCustomerId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Removes a vaulted card so it can no longer be charged.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists PayPal's own transaction records over the whole date range (transparently chunked to PayPal's
    /// per-request window limit and paginated), for reconciliation against eShop orders.
    /// </summary>
    Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
