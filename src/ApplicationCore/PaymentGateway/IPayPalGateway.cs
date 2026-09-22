using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.PaymentGateway;

/// <summary>
/// Provider-neutral seam over the PayPal Server SDK. Every PayPal interaction goes through here; the
/// implementation (Infrastructure) is the only place that references the SDK, and it translates all
/// SDK exceptions to <see cref="PayPalGatewayException"/>.
/// </summary>
public interface IPayPalGateway
{
    /// <summary>Merchant currency (ISO-4217) from configuration.</summary>
    string Currency { get; }

    /// <summary>
    /// Creates a PayPal order with intent=AUTHORIZE and no payment source. Idempotent per
    /// <paramref name="reference"/> (used as PayPal-Request-Id). Returns the PayPal order id.
    /// </summary>
    Task<string> CreateOrderAsync(
        string reference,
        GatewayMoney amount,
        string invoiceId,
        string customId,
        string description,
        CancellationToken ct);

    /// <summary>
    /// Authorizes a previously-created PayPal order by supplying a payment source (a raw card, or a
    /// vaulted card via <paramref name="vaultId"/>). Places the hold. Idempotent per
    /// <paramref name="reference"/>.
    /// </summary>
    Task<AuthorizationResult> AuthorizeOrderAsync(
        string reference,
        string payPalOrderId,
        CardDetails? card,
        string? vaultId,
        CancellationToken ct);

    /// <summary>Re-reads a PayPal order to recover an authorization after an unknown-outcome transport fault.</summary>
    Task<AuthorizationResult?> TryReadOrderAuthorizationAsync(string payPalOrderId, CancellationToken ct);

    /// <summary>Captures (takes) the money for an authorization. Idempotent per <paramref name="reference"/>.</summary>
    Task<CaptureResult> CaptureAsync(
        string reference,
        string authorizationId,
        GatewayMoney amount,
        CancellationToken ct);

    /// <summary>Renews a stale authorization. Idempotent per <paramref name="reference"/>.</summary>
    Task<ReauthorizeResult> ReauthorizeAsync(
        string reference,
        string authorizationId,
        GatewayMoney amount,
        CancellationToken ct);

    /// <summary>Voids (releases) an authorization before capture. Idempotent per <paramref name="reference"/>.</summary>
    Task VoidAsync(string reference, string authorizationId, CancellationToken ct);

    /// <summary>
    /// Refunds a captured payment, in full or in part. <paramref name="idempotencyKey"/> is the
    /// caller-supplied key sent as PayPal-Request-Id — repeating it never refunds twice.
    /// </summary>
    Task<RefundResult> RefundAsync(
        string idempotencyKey,
        string captureId,
        GatewayMoney amount,
        string customId,
        CancellationToken ct);

    /// <summary>
    /// Vaults a raw card (setup-token then payment-token) for later reuse. Returns the vault id and a
    /// safe display of the card. <paramref name="merchantCustomerId"/> associates the token to the shopper.
    /// </summary>
    Task<VaultedCard> VaultCardAsync(
        string reference,
        CardDetails card,
        string merchantCustomerId,
        CancellationToken ct);

    /// <summary>Removes a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct);

    /// <summary>
    /// Lists PayPal's own transaction records for a date range, paging through the whole range so
    /// reconciliation is not limited to the first page.
    /// </summary>
    Task<ReconciliationResult> SearchTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        int maxPages,
        CancellationToken ct);
}
