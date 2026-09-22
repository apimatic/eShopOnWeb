using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Abstraction over the PayPal payment processor. The concrete implementation lives in Infrastructure
/// and is the sole place the PayPal SDK is used; it owns the configured currency and formats amounts to
/// the cent. All methods translate provider/transport failures into <see cref="PaymentGatewayException"/>
/// (or <see cref="BrowserApprovalRequiredException"/> for a challenge that would need a browser).
/// </summary>
public interface IPaymentGateway
{
    /// <summary>Merchant currency (ISO-4217), from configuration.</summary>
    string Currency { get; }

    /// <summary>Create a PayPal order (intent AUTHORIZE) and place a hold equal to the amount. No money moves.</summary>
    Task<AuthorizeResult> AuthorizeAsync(AuthorizeCommand command, CancellationToken ct);

    /// <summary>
    /// Capture a held authorization at fulfilment; money moves. Returns fee/net as PayPal reports them.
    /// <paramref name="payPalOrderId"/> lets the gateway re-read the order to recover the capture if the
    /// transport fails after the request may have been received.
    /// </summary>
    Task<CaptureResult> CaptureAsync(string authorizationId, string payPalOrderId, decimal amount,
        string requestId, CancellationToken ct);

    /// <summary>Renew a stale authorization before capture. Returns the new authorization id.</summary>
    Task<ReauthorizeResult> ReauthorizeAsync(string authorizationId, decimal amount, string requestId, CancellationToken ct);

    /// <summary>Void a held authorization before fulfilment; the shopper's funds are released.</summary>
    Task<VoidResult> VoidAsync(string authorizationId, string requestId, CancellationToken ct);

    /// <summary>Refund a captured payment in full (<paramref name="amount"/> null) or in part.</summary>
    Task<RefundResult> RefundAsync(string captureId, decimal? amount, string idempotencyKey, CancellationToken ct);

    /// <summary>Vault a card and return its safe descriptor. Full card details are never stored.</summary>
    Task<VaultCardResult> VaultCardAsync(CardDetails card, string? customerId, string requestId, CancellationToken ct);

    /// <summary>Remove a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken ct);

    /// <summary>PayPal's own record of transactions for a date range, covering the whole range across pages.</summary>
    Task<TransactionSearchResult> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
