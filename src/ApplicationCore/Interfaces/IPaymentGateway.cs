using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The payment provider abstraction. The application talks money through this interface; the
/// PayPal SDK lives entirely behind the implementation in Infrastructure. All amounts are decimal
/// in <see cref="Currency"/>. Implementations translate provider failures into
/// <see cref="Exceptions.PaymentGatewayException"/> / <see cref="Exceptions.PaymentChallengeRequiredException"/>.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>The configured settlement currency (ISO-4217), from configuration.</summary>
    string Currency { get; }

    /// <summary>
    /// Authorize (hold) <paramref name="amount"/> against a one-off card or a saved card
    /// (<paramref name="vaultTokenId"/>). Exactly one of <paramref name="card"/> /
    /// <paramref name="vaultTokenId"/> is supplied. Does not capture.
    /// </summary>
    Task<GatewayAuthorization> AuthorizeAsync(string eShopOrderReference, decimal amount, string currency,
        CardDetails? card, string? vaultTokenId, string idempotencyKey, CancellationToken ct);

    /// <summary>Capture the full authorized amount (money taken).</summary>
    Task<GatewayCapture> CaptureAsync(string authorizationId, string idempotencyKey, CancellationToken ct);

    /// <summary>Renew a stale authorization before capture.</summary>
    Task<GatewayReauthorization> ReauthorizeAsync(string authorizationId, string idempotencyKey, CancellationToken ct);

    /// <summary>Release a held authorization (cancel before fulfilment).</summary>
    Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct);

    /// <summary>
    /// Refund a captured payment. <paramref name="amount"/> null = full refund of the remaining
    /// captured amount; a value = partial refund. <paramref name="idempotencyKey"/> is the
    /// caller-supplied key that dedupes a repeated request at the provider.
    /// </summary>
    Task<GatewayRefund> RefundAsync(string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken ct);

    /// <summary>Vault a card and return a safe description of it plus the vault token id.</summary>
    Task<GatewaySavedCard> VaultCardAsync(CardDetails card, string idempotencyKey, CancellationToken ct);

    /// <summary>Delete a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken ct);

    /// <summary>
    /// List the provider's own transaction records over a date range (covering the whole range,
    /// paging as needed) for reconciliation against eShop orders.
    /// </summary>
    Task<IReadOnlyList<GatewayTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
