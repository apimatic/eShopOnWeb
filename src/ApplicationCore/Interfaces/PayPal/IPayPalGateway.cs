using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

/// <summary>
/// The single boundary to PayPal. Every method translates the SDK's failures into
/// <see cref="Exceptions.PayPalException"/>, so callers see one error type with a classified status.
/// All write operations take a caller-owned request id used as PayPal's idempotency key.
/// </summary>
public interface IPayPalGateway
{
    /// <summary>Create an order with intent=AUTHORIZE and place a hold using a raw card. No browser approval.</summary>
    Task<AuthorizationResult> AuthorizeWithCardAsync(decimal amount, string currencyCode, CardDetails card,
        string requestId, CancellationToken ct = default);

    /// <summary>Create an order with intent=AUTHORIZE and place a hold using a previously vaulted card token.</summary>
    Task<AuthorizationResult> AuthorizeWithVaultedCardAsync(decimal amount, string currencyCode, string vaultTokenId,
        string requestId, CancellationToken ct = default);

    /// <summary>Capture an authorization (take the money) at fulfilment.</summary>
    Task<CaptureResult> CaptureAsync(string authorizationId, string requestId, CancellationToken ct = default);

    /// <summary>Renew a stale authorization before capture. Throws <see cref="Exceptions.PayPalException"/> when it can no longer be renewed.</summary>
    Task<ReauthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currencyCode,
        string requestId, CancellationToken ct = default);

    /// <summary>Void an authorization (release the hold) before capture.</summary>
    Task VoidAsync(string authorizationId, string requestId, CancellationToken ct = default);

    /// <summary>Refund a captured payment. A null amount refunds the full remaining balance.</summary>
    Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currencyCode, string requestId,
        CancellationToken ct = default);

    /// <summary>Vault a card and return a reusable token plus a safe descriptor. Never returns/stores a PAN.</summary>
    Task<VaultCardResult> VaultCardAsync(CardDetails card, string requestId, CancellationToken ct = default);

    /// <summary>Delete a vaulted card token so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken ct = default);

    /// <summary>
    /// List PayPal's own transaction records across the whole date range, following pagination to the
    /// last page rather than stopping at the first.
    /// </summary>
    Task<IReadOnlyList<TransactionRecord>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct = default);
}
